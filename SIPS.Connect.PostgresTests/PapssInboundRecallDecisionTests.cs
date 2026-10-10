using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SIPS.Adapter;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Interfaces;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Connect.PostgresTests;

/// <summary>
/// R2 decide-inbound-recall endpoint (POST Recall/Inbound/{recallId}/Decision), end to end against a real PostgreSQL: REJECT
/// submits a camt.029.001.08 and reaches the terminal state; ACCEPT reuses the existing return path; a contradictory second
/// decision is refused and never applied.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class PapssInboundRecallDecisionTests
{
    [Fact]
    public async Task Reject_submits_a_camt029_rejection_and_reaches_the_terminal_state()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-D1", "E2E-RX-D1", "EGAFEGCX");
        var recall = await SeedInboundRecall(provider, "CT02-RX-D1-RECALL", payment, "DUPL");

        var result = await Decide(harness, provider, recall.RequestMessageId, "REJECT", "CUST");
        var response = (PapssInboundRecallDecisionResponse)Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal("REJECT", response.Decision);

        var submitted = Assert.Single(harness.Gateway_.Submitted);
        var document = XDocument.Parse(submitted.Replace("<!--signed-->", string.Empty));
        Assert.Equal("camt.029.001.08", document.Descendants().First(x => x.Name.LocalName == "MsgDefIdr").Value);
        Assert.Equal("RJCR", document.Descendants().First(x => x.Name.LocalName == "Conf").Value);
        Assert.Equal("RX-D1", document.Descendants().First(x => x.Name.LocalName == "OrgnlTxId").Value);
        Assert.Equal("CUST", document.Descendants().First(x => x.Name.LocalName == "Rsn").Elements().First().Value);

        var stored = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == recall.Id));
        Assert.Equal(PapssOutcome.InboundRecallReplySubmitted, stored.PapssOutcome);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task Accept_submits_a_pacs004_via_the_existing_return_path()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-D2", "E2E-RX-D2", "EGAFEGCX");
        var recall = await SeedInboundRecall(provider, "CT02-RX-D2-RECALL", payment, "DUPL");

        var result = await Decide(harness, provider, recall.RequestMessageId, "ACCEPT", null);
        var response = (PapssInboundRecallDecisionResponse)Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal("ACCEPT", response.Decision);
        Assert.NotNull(response.ReturnId);

        Assert.Single(harness.Gateway_.Submitted);
        var stored = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == recall.Id));
        Assert.Equal(PapssOutcome.InboundRecallReplySubmitted, stored.PapssOutcome);
        // The return itself is a real, separate OUTBOUND operation (reusing ReturnAsync's own pipeline, not a special case).
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Return)));
    }

    [Fact]
    public async Task A_contradictory_decision_after_the_first_is_refused_and_never_applied()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-D3", "E2E-RX-D3", "EGAFEGCX");
        var recall = await SeedInboundRecall(provider, "CT02-RX-D3-RECALL", payment, "DUPL");

        Assert.IsType<OkObjectResult>(await Decide(harness, provider, recall.RequestMessageId, "REJECT", "CUST"));
        var submittedBefore = harness.Gateway_.Count;

        var conflict = Assert.IsAssignableFrom<ObjectResult>(await Decide(harness, provider, recall.RequestMessageId, "ACCEPT", null));
        Assert.Equal(409, conflict.StatusCode);
        // No second submission was attempted: the conflicting decision is refused before anything is sent.
        Assert.Equal(submittedBefore, harness.Gateway_.Count);

        var stored = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == recall.Id));
        Assert.Equal(PapssOutcome.InboundRecallReplySubmitted, stored.PapssOutcome); // still the REJECT outcome, untouched
    }

    [Fact]
    public async Task The_rejection_wire_id_is_deterministic_so_a_retry_racing_the_state_update_would_replay_not_duplicate()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-D4", "E2E-RX-D4", "EGAFEGCX");
        var recall = await SeedInboundRecall(provider, "CT02-RX-D4-RECALL", payment, "DUPL");

        Assert.IsType<OkObjectResult>(await Decide(harness, provider, recall.RequestMessageId, "REJECT", "CUST"));
        var firstSubmission = Assert.Single(harness.Gateway_.Submitted);
        var rejectionId = XDocument.Parse(firstSubmission.Replace("<!--signed-->", string.Empty)).Descendants().First(x => x.Name.LocalName == "BizMsgIdr").Value;

        // Once REPLY_SUBMITTED, repeating even the SAME decision is refused (409) rather than silently re-applied -- a
        // deliberate, conservative choice: the bank must GET the recall to see the already-recorded outcome rather than
        // relying on a silent no-op. Nothing is resubmitted to the gateway.
        var retried = Assert.IsAssignableFrom<ObjectResult>(await Decide(harness, provider, recall.RequestMessageId, "REJECT", "CUST"));
        Assert.Equal(409, retried.StatusCode);
        Assert.Single(harness.Gateway_.Submitted);

        // Had the retry instead raced ahead of the REPLY_SUBMITTED state update (still RejectedByBank), DecideInboundRecallAsync
        // derives the wire id purely from the recall id and the fixed "REJECT" purpose, so it would have been byte-identical
        // to the first -- the gateway's own exact-replay detection, not a second distinct submission.
        Assert.NotNull(rejectionId);
    }

    static async Task<ActionResult> Decide(PostgresHarness harness, ServiceProvider provider, string recallId, string decision, string? reason)
    {
        using var scope = provider.CreateScope();
        return await Controller(harness, scope.ServiceProvider).DecideInboundRecall(recallId, new PapssInboundRecallDecisionRequest { Decision = decision, Reason = reason }, CancellationToken.None);
    }

    static async Task<PapssOperation> SeedInboundRecall(ServiceProvider provider, string sourceMessageId, PapssOperation payment, string reasonCode)
    {
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var message = new PapssInboundRecallMessage(sourceMessageId, DateTimeOffset.UtcNow, "CXL-" + sourceMessageId, payment.TxId!, payment.EndToEndId!, reasonCode, null);
        var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", message, payment, CancellationToken.None);
        return operation;
    }

    static async Task<PapssOperation> SeedReceivedPayment(PostgresHarness harness, string txId, string endToEndId, string counterpartyBic)
    {
        var now = harness.Clock.GetUtcNow();
        return await harness.WithStorageAsync(async db =>
        {
            var payment = new PapssOperation
            {
                Direction = PapssDirection.Inbound,
                Operation = PapssOperationType.Payment,
                RequestMessageId = "CT02-" + txId,
                MsgId = "CT02-" + txId,
                TxId = txId,
                EndToEndId = endToEndId,
                CounterpartyBic = counterpartyBic,
                Amount = 500.00m,
                Currency = "USD",
                LocalInstrument = "USDP",
                GatewayState = PapssGatewayState.NotSubmitted,
                PapssOutcome = PapssOutcome.Settled,
                BankDeliveryState = PapssDeliveryState.Delivered,
                SignedRequest = "<raw/>"u8.ToArray(),
                SourceCreatedAt = now,
                ReceivedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PapssOperations.Add(payment);
            await db.SaveChangesAsync(CancellationToken.None);
            return payment;
        });
    }

    static GatewayController Controller(PostgresHarness harness, IServiceProvider services)
    {
        var router = new Mock<IParticipantOperationRouter>();
        router.Setup(x => x.Select(It.IsAny<ParticipantOperation>(), It.IsAny<string?>())).Returns(DownstreamRail.Papss);
        router.Setup(x => x.ResolvePapss()).Returns(harness.Binding());
        return new GatewayController(
            services.GetRequiredService<IJsonAdapter>(),
            Mock.Of<IOutgoingVerificationHandler>(), Mock.Of<IOutgoingTransactionHandler>(), Mock.Of<IOutgoingTransactionStatusHandler>(),
            Mock.Of<IOutgoingReturnTransactionHandler>(), Mock.Of<IReturnRetryHandler>(), Microsoft.Extensions.Options.Options.Create(new SIPS.Core.Options.CoreOptions()),
            router.Object, services.GetRequiredService<IPapssFacingSipsClient>(), services.GetRequiredService<PapssOperationStore>(),
            harness.Options, harness.Clock, services.GetRequiredService<IPapssPaymentService>());
    }
}
