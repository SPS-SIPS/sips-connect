using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using SIPS.Adapter;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.ISO20022.Interfaces;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.Connect.Tests;
using Xunit;

namespace SIPS.Connect.PostgresTests;

/// <summary>R1 outbound recall (camt.056) and its answers against a real PostgreSQL.</summary>
[Trait("Category", "Postgres")]
public sealed class PapssRecallOperationTests
{
    private static readonly XNamespace D = "urn:iso:std:iso:20022:tech:xsd:camt.056.001.08";

    [Fact]
    public async Task Migration_adds_the_one_open_recall_per_payment_index()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var history = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migrationid LIKE '%_AddPapssRecall'", connection);
        Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);
        await using var unresolvedHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migrationid LIKE '%_AddPapssRecallUnresolvedOutcome'", connection);
        Assert.Equal(1L, (long)(await unresolvedHistory.ExecuteScalarAsync())!);
        await using var index = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_papss_op_open_recall'", connection);
        var definition = (string)(await index.ExecuteScalarAsync())!;
        Assert.StartsWith("CREATE UNIQUE INDEX", definition);
        Assert.Contains("(originaloperationid)", definition);
        Assert.Contains("RECALL_PENDING", definition);
        Assert.Contains("RECALL_ACCEPTED_BY_PAPSS", definition);
        // RECALL_OUTCOME_UNRESOLVED (S3 recovery) is also OPEN; RECALL_ABANDONED (the manual close outcome) and the other
        // (OUTBOUND) RECALL_REJECTED_* outcomes are final and must never appear in the open-recall filter. Quoted exact
        // tokens: R2 added its own, unrelated 'INBOUND_RECALL_REJECTED_BY_BANK' to the SAME (shared) filter, which contains
        // the substring "RECALL_REJECTED" too -- see PapssInboundRecallOperationTests for its own regression guard.
        Assert.Contains("RECALL_OUTCOME_UNRESOLVED", definition);
        Assert.DoesNotContain("'RECALL_REJECTED_BY_PAPSS'", definition);
        Assert.DoesNotContain("'RECALL_REJECTED_BY_BENEFICIARY'", definition);
        Assert.DoesNotContain("'RECALL_ABANDONED'", definition);
    }

    [Fact]
    public async Task Recall_is_persisted_before_submission_with_the_exact_camt056_and_follows_the_admission()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R1");
        string? storedAtSubmit = null;
        harness.Gateway_.OnSubmit = (_, xml) =>
        {
            var id = PostgresHarness.Header(xml.Replace("<!--signed-->", string.Empty), "BizMsgIdr");
            storedAtSubmit = Task.Run(() => harness.WithStorageAsync(async db =>
            {
                var row = await db.PapssOperations.AsNoTracking().SingleAsync(x => x.RequestMessageId == id);
                return $"{row.Operation}|{row.GatewayState}|{row.PapssOutcome}|{row.OriginalOperationId == payment.Id}|{Encoding.UTF8.GetString(row.SignedRequest!) == xml}";
            })).GetAwaiter().GetResult();
            return "RECEIVED_AND_DURABLY_ADMITTED";
        };

        var response = await Recall(harness, provider, Body("TX-R1"));
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", response["code"]!.GetValue<string>());
        Assert.Equal("Recall|Submitting|RecallPending|True|True", storedAtSubmit);
        var recallId = response["requestMessageId"]!.GetValue<string>();
        Assert.Matches("^SIPS-[0-9a-f]{24}$", recallId);

        var recall = await StoredRecall(harness, recallId);
        Assert.Equal((PapssDirection.Outbound, PapssGatewayState.Admitted, PapssOutcome.RecallPending, "DUPL"), (recall.Direction, recall.GatewayState, recall.PapssOutcome, recall.Reason));
        Assert.Equal(("TX-R1", "E2E-TX-R1", 10m, "USD"), (recall.OriginalTxId, recall.OriginalEndToEndId, recall.Amount, recall.Currency));
        // PAPSS-confirmed 30-day beneficiary deadline, record-only, from the admission.
        Assert.Equal(harness.Clock.GetUtcNow().AddDays(30), recall.DeadlineAt);

        // The exact camt.056.001.08 contract.
        var xml = XDocument.Parse(Encoding.UTF8.GetString(recall.SignedRequest!).Replace("<!--signed-->", string.Empty));
        Assert.Equal((recallId, "camt.056.001.08", PostgresHarness.Gateway), (PostgresHarness.Header(xml.ToString(), "BizMsgIdr"), PostgresHarness.Header(xml.ToString(), "MsgDefIdr"),
            xml.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == "To").Descendants().First(x => x.Name.LocalName == "Id").Value));
        Assert.Equal(PostgresHarness.LocalBic, xml.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == "Fr").Descendants().First(x => x.Name.LocalName == "Id").Value);
        var request = xml.Descendants(D + "FIToFIPmtCxlReq").Single();
        Assert.Equal(recallId, request.Element(D + "Assgnmt")!.Element(D + "Id")!.Value);
        Assert.Equal(PostgresHarness.LocalBic, request.Element(D + "Assgnmt")!.Descendants(D + "BICFI").Single().Value);
        Assert.Equal(PostgresHarness.Gateway, request.Element(D + "Assgnmt")!.Element(D + "Assgne")!.Descendants(D + "Id").Single().Value);
        var tx = request.Element(D + "Undrlyg")!.Element(D + "TxInf")!;
        Assert.Equal(recallId, tx.Element(D + "CxlId")!.Value);
        Assert.Equal((payment.MsgId, "pacs.008.001.10"), (tx.Element(D + "OrgnlGrpInf")!.Element(D + "OrgnlMsgId")!.Value, tx.Element(D + "OrgnlGrpInf")!.Element(D + "OrgnlMsgNmId")!.Value));
        Assert.Equal(("E2E-TX-R1", "TX-R1"), (tx.Element(D + "OrgnlEndToEndId")!.Value, tx.Element(D + "OrgnlTxId")!.Value));
        Assert.Equal(("USD", "10.00"), (tx.Element(D + "OrgnlIntrBkSttlmAmt")!.Attribute("Ccy")!.Value, tx.Element(D + "OrgnlIntrBkSttlmAmt")!.Value));
        Assert.Equal("DUPL", tx.Descendants(D + "Cd").Single().Value);

        // Submitting the recall changes nothing on the payment.
        Assert.Equal(Snapshot(payment), Snapshot(await Stored(harness, "TX-R1")));
    }

    [Fact]
    public async Task Recall_is_refused_for_unknown_received_unsettled_expired_or_already_recalled_payments()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R2");
        await Pay(harness, provider, Payment("TX-R2P")); // admitted, not settled
        await RecordInbound(harness, provider, "CT02-IN-R2", "TX-R2IN", "E2E-TX-R2IN");
        var submitted = harness.Gateway_.Count;

        Assert.Equal((404, "ORIGINAL_PAYMENT_NOT_FOUND"), await Refusal(harness, provider, Body("TX-NOPE")));
        Assert.Equal((422, "RECALL_NOT_ALLOWED"), await Refusal(harness, provider, Body("TX-R2IN")));
        Assert.Equal((409, "ORIGINAL_NOT_SETTLED"), await Refusal(harness, provider, Body("TX-R2P")));
        Assert.Equal((422, "PAPSS_VALIDATION_FAILED"), await Refusal(harness, provider, Body("TX-R2", reason: "DUPLICATE")));
        Assert.Equal((422, "PAPSS_VALIDATION_FAILED"), await Refusal(harness, provider, new JsonObject { ["txId"] = "TX-R2" }));
        Assert.Equal(submitted, harness.Gateway_.Count);

        // By endToEndId as well as txId; then a second recall while the first is open is refused.
        var first = await Recall(harness, provider, new JsonObject { ["rail"] = "PAPSS", ["endToEndId"] = "E2E-TX-R2", ["reason"] = "FRAD" });
        var open = await Refusal(harness, provider, Body("TX-R2", reason: "DUPL"));
        Assert.Equal((409, "RECALL_ALREADY_OPEN"), open);
        Assert.Equal(submitted + 1, harness.Gateway_.Count);
        var message = await RefusalMessage(harness, provider, Body("TX-R2"));
        Assert.Contains(first["requestMessageId"]!.GetValue<string>(), message);

        // The database refuses a second open recall even if the application check is bypassed (concurrent requests).
        var payment = await Stored(harness, "TX-R2");
        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
            var race = await Assert.ThrowsAsync<ParticipantRailException>(() => store.CreateOutboundRecallAsync(
                new PapssSignedMessage(PapssFacingSipsClient.Id(), "<x/>", DateTimeOffset.UtcNow), payment, "DUPL", "f", CancellationToken.None));
            Assert.Equal("RECALL_ALREADY_OPEN", race.Code);
        }
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.Operation == PapssOperationType.Recall)));

        // PAPSS-confirmed 30-day recall window after settlement.
        await SettledPayment(harness, provider, "TX-R2OLD");
        harness.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal((409, "RECALL_WINDOW_EXPIRED"), await Refusal(harness, provider, Body("TX-R2OLD")));
    }

    /// <summary>
    /// Regression guard for finding S4: recalling an already-RETURNED or already-REJECTED payment must stay refused
    /// (ORIGINAL_NOT_SETTLED) even when PapssFacing:Recall:RequireSettledOriginal is relaxed to allow unsettled payments -
    /// the guard is unconditional for these two outcomes, not merely "must be SETTLED".
    /// </summary>
    [Fact]
    public async Task Recall_of_an_already_returned_or_already_rejected_payment_stays_refused_even_when_unsettled_is_allowed()
    {
        await using var harness = await PostgresHarness.CreateAsync(o => o.Recall.RequireSettledOriginal = false);
        await using var provider = harness.BuildProvider();

        // Settled, then a pacs.004 returns it: PapssOutcome.Returned.
        await SettledPayment(harness, provider, "TX-R22");
        await InboundReturn(provider, harness, PostgresHarness.InboundReturn("CT02-RTN-221", "RTN-22", "TX-R22", "E2E-TX-R22"));
        Assert.Equal(PapssOutcome.Returned, (await Stored(harness, "TX-R22")).PapssOutcome);
        Assert.Equal((409, "ORIGINAL_NOT_SETTLED"), await Refusal(harness, provider, Body("TX-R22")));

        // Admitted, then PAPSS rejects it (RJCT): PapssOutcome.Rejected.
        await Pay(harness, provider, Payment("TX-R23"));
        var payment = await Stored(harness, "TX-R23");
        await StatusCallback(provider, harness, PostgresHarness.StatusReport("PAPSS-S-TX-R23", payment.MsgId!, "pacs.008.001.10", "TX-R23", "E2E-TX-R23", "RJCT", reason: "AC04"));
        Assert.Equal(PapssOutcome.Rejected, (await Stored(harness, "TX-R23")).PapssOutcome);
        Assert.Equal((409, "ORIGINAL_NOT_SETTLED"), await Refusal(harness, provider, Body("TX-R23")));

        // No recall was ever stored for either payment.
        Assert.Equal(0, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.Operation == PapssOperationType.Recall)));
    }

    [Fact]
    public async Task Unsettled_payment_may_be_recalled_only_when_configured()
    {
        await using var harness = await PostgresHarness.CreateAsync(o => o.Recall.RequireSettledOriginal = false);
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-R3"));
        var response = await Recall(harness, provider, Body("TX-R3"));
        Assert.Equal(PapssOutcome.RecallPending, (await StoredRecall(harness, response["requestMessageId"]!.GetValue<string>())).PapssOutcome);
    }

    [Fact]
    public async Task Recall_id_is_idempotent_with_replay_resubmission_and_conflict()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R4");
        var recallId = PapssFacingSipsClient.Id();
        var before = harness.Gateway_.Count;

        harness.Gateway_.OnSubmit = (call, _) => call == before + 1 ? throw new HttpRequestException("connection reset") : "EXACT_REPLAY";
        using (var scope = provider.CreateScope())
            Assert.Equal(503, Assert.IsType<ObjectResult>(await Controller(harness, scope.ServiceProvider).Recall(Body("TX-R4", recallId: recallId), CancellationToken.None)).StatusCode);
        Assert.Equal(PapssGatewayState.SubmissionUnknown, (await StoredRecall(harness, recallId)).GatewayState);

        // Same recallId + same content: the stored signed bytes are re-submitted unchanged.
        var replay = await Recall(harness, provider, Body("TX-R4", recallId: recallId));
        Assert.Equal((recallId, "EXACT_REPLAY"), (replay["requestMessageId"]!.GetValue<string>(), replay["code"]!.GetValue<string>()));
        Assert.Equal(harness.Gateway_.Submitted[before], harness.Gateway_.Submitted[before + 1]);
        Assert.Equal(PapssGatewayState.Admitted, (await StoredRecall(harness, recallId)).GatewayState);

        // Admitted: answered from the store.
        var again = await Recall(harness, provider, Body("TX-R4", recallId: recallId));
        Assert.Equal("EXACT_REPLAY", again["code"]!.GetValue<string>());
        Assert.Equal(before + 2, harness.Gateway_.Count);

        // Same recallId, different content: DUPLICATE_CONFLICT without calling the gateway.
        Assert.Equal((400, "DUPLICATE_CONFLICT"), await Refusal(harness, provider, Body("TX-R4", reason: "FRAD", recallId: recallId)));
        Assert.Equal(before + 2, harness.Gateway_.Count);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.Operation == PapssOperationType.Recall)));
    }

    [Fact]
    public async Task Papss_accp_moves_only_the_recall_and_never_touches_the_payment()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R5");
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var bankCalls = harness.Bank.Count;
        var recallId = (await Recall(harness, provider, Body("TX-R5")))["requestMessageId"]!.GetValue<string>();
        var paymentBefore = Snapshot(await Stored(harness, "TX-R5"));
        var paymentEventsBefore = await PaymentEvents(harness, payment.Id);

        // The answer carries the ORIGINAL PAYMENT's TxId/EndToEndId; OrgnlMsgNmId camt.056 routes it to the recall.
        var accp = await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-51", recallId, "TX-R5", "E2E-TX-R5", "ACCP", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic, statusId: "PS51"));
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal((recall.Id, PapssCorrelation.MessageId, PapssEventDisposition.Applied, true), (accp.Operation!.Id, accp.Correlation, accp.Disposition, accp.Pushed));
        Assert.Equal((PapssOutcome.RecallAcceptedByPapss, "ACCP", PapssDeliveryState.Pending), (recall.PapssOutcome, recall.PaymentStatus, recall.BankDeliveryState));
        Assert.Null(recall.CompletedAt);

        // Defence in depth: even the payment ingestion path routes a camt.056 answer to the recall.
        using (var scope = provider.CreateScope())
        {
            var xml = GatewayRecallXml.RecallStatus("PAPSS-RS-52", recallId, "TX-R5", "E2E-TX-R5", "ACCP", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic);
            var direct = await scope.ServiceProvider.GetRequiredService<PapssOperationStore>().IngestPaymentStatusAsync(xml, PapssPaymentMessages.ParseStatusReport(xml), CancellationToken.None);
            Assert.Equal((recall.Id, PapssEventDisposition.NotAdvancing), (direct.Operation!.Id, direct.Disposition));
        }

        // ISOLATION: the payment row and its status history are exactly as before.
        Assert.Equal(paymentBefore, Snapshot(await Stored(harness, "TX-R5")));
        Assert.Equal(paymentEventsBefore, await PaymentEvents(harness, payment.Id));
        Assert.Equal(2, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.OperationId == recall.Id && x.EventType == PapssEventTypes.RecallStatus)));

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var call = Assert.Single(harness.Bank.Calls.Skip(bankCalls));
        Assert.Equal((recallId, "TX-R5", "E2E-TX-R5", "RECALL_ACCEPTED_BY_PAPSS", "PS51", "PAPSS-RS-51"),
            (Text(call.Body, "recallId"), Text(call.Body, "txId"), Text(call.Body, "endToEndId"), Text(call.Body, "outcome"), Text(call.Body, "responderId"), Text(call.Body, "sourceMessageId")));
        Assert.Equal(($"{recallId}:RECALL_ACCEPTED_BY_PAPSS", recallId), (call.Headers!["X-Idempotency-Key"], call.Headers["X-Recall-Id"]));
        Assert.False(call.Body.ContainsKey("status"));

        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-R5", null, CancellationToken.None));
        Assert.Equal(("COMPLETED", "SETTLED", "ACSC"), (Text(lookup, "status"), Text(lookup, "paymentOutcome"), Text(lookup, "paymentStatus")));
        Assert.Equal(["ACSC"], lookup["statusHistory"]!.AsArray().Select(h => h!["status"]!.GetValue<string>()));
        var summary = Assert.Single(lookup["recalls"]!.AsArray())!;
        Assert.Equal((recallId, "PENDING", "RECALL_ACCEPTED_BY_PAPSS"), (Text(summary, "recallId"), Text(summary, "status"), Text(summary, "papssOutcome")));
    }

    [Fact]
    public async Task Papss_rjct_rejects_the_recall_and_a_new_recall_is_then_allowed()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R6");
        var paymentBefore = Snapshot(await Stored(harness, "TX-R6"));
        var first = (await Recall(harness, provider, Body("TX-R6")))["requestMessageId"]!.GetValue<string>();

        var rjct = await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-61", first, "TX-R6", "E2E-TX-R6", "RJCT", reason: "1028", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssEventDisposition.Applied, rjct.Disposition);
        var rejected = await StoredRecall(harness, first);
        Assert.Equal((PapssOutcome.RecallRejectedByPapss, "RJCT", "1028"), (rejected.PapssOutcome, rejected.PaymentStatus, rejected.StatusReasonCode));
        Assert.NotNull(rejected.CompletedAt);
        Assert.Equal(paymentBefore, Snapshot(await Stored(harness, "TX-R6")));

        // A late ACCP after the RJCT is flagged, not applied.
        var late = await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-62", first, "TX-R6", "E2E-TX-R6", "ACCP", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal((PapssEventDisposition.Conflict, false), (late.Disposition, late.Pushed));
        Assert.True((await StoredRecall(harness, first)).StatusConflict);

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var pushed = harness.Bank.Calls.Where(c => c.Body.ContainsKey("recallId")).ToList();
        var call = Assert.Single(pushed);
        Assert.Equal(("RECALL_REJECTED_BY_PAPSS", "1028"), (Text(call.Body, "outcome"), Text(call.Body, "reasonCode")));

        // Terminal: a new recall of the same payment is allowed.
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await Recall(harness, provider, Body("TX-R6", reason: "FRAD")))["requestMessageId"]!.GetValue<string>();
        Assert.NotEqual(first, second);
        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-R6", null, CancellationToken.None));
        Assert.Equal([(first, "REJECTED", "RECALL_REJECTED_BY_PAPSS"), (second, "PENDING", "RECALL_PENDING")],
            lookup["recalls"]!.AsArray().Select(r => (Text(r!, "recallId"), Text(r!, "status"), Text(r!, "papssOutcome"))));
    }

    [Fact]
    public async Task Camt029_rejects_the_recall_for_the_beneficiary_and_pushes_it_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R7");
        var paymentBefore = Snapshot(await Stored(harness, "TX-R7"));
        var paymentEventsBefore = await PaymentEvents(harness, payment.Id);
        var recallId = (await Recall(harness, provider, Body("TX-R7")))["requestMessageId"]!.GetValue<string>();
        await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-71", recallId, "TX-R7", "E2E-TX-R7", "ACCP", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));

        var camt029 = GatewayRecallXml.Resolution("CT02-CXL-71", recallId, payment.MsgId!, "TX-R7", "E2E-TX-R7", reason: "CUST", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic, responderId: "UG1003CXL71");
        var result = await Resolution(provider, harness, camt029);
        Assert.Equal((PapssCorrelation.MessageId, PapssEventDisposition.Applied, true), (result.Correlation, result.Disposition, result.Pushed));
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal((PapssOutcome.RecallRejectedByBeneficiary, "RJCR", "CUST"), (recall.PapssOutcome, recall.PaymentStatus, recall.StatusReasonCode));
        Assert.NotNull(recall.CompletedAt);
        Assert.Equal(camt029, Encoding.UTF8.GetString(recall.SignedResponse!));

        // Redelivery of the same camt.029: stored once, pushed once.
        Assert.True((await Resolution(provider, harness, camt029)).Duplicate);
        Assert.Equal(paymentBefore, Snapshot(await Stored(harness, "TX-R7")));
        Assert.Equal(paymentEventsBefore, await PaymentEvents(harness, payment.Id));

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var pushes = harness.Bank.Calls.Where(c => c.Body.ContainsKey("recallId")).ToList();
        Assert.Equal(["RECALL_ACCEPTED_BY_PAPSS", "RECALL_REJECTED_BY_BENEFICIARY"], pushes.Select(c => Text(c.Body, "outcome")));
        var last = pushes[^1];
        Assert.Equal(("CUST", "UG1003CXL71", "CT02-CXL-71", recallId), (Text(last.Body, "reasonCode"), Text(last.Body, "responderId"), Text(last.Body, "sourceMessageId"), Text(last.Body, "recallId")));
        Assert.Equal(PapssDeliveryState.Delivered, (await StoredRecall(harness, recallId)).BankDeliveryState);

        var view = await Lookup(harness, provider, c => c.GetRecall(recallId, null, CancellationToken.None));
        Assert.Equal(("RECALL", "REJECTED", "RECALL_REJECTED_BY_BENEFICIARY", "SETTLED"), (Text(view, "operation"), Text(view, "status"), Text(view, "papssOutcome"), Text(view, "originalPaymentOutcome")));
        Assert.Equal(["ACCP", "RJCR"], view["statusHistory"]!.AsArray().Select(h => h!["status"]!.GetValue<string>()));
        Assert.Equal((payment.RequestMessageId, "TX-R7", "DUPL", "CUST"), (Text(view, "originalRequestMessageId"), Text(view, "originalTxId"), Text(view, "reason"), Text(view, "statusReasonCode")));
        Assert.False(view.ContainsKey("paymentOutcome"));
        Assert.False(view["responseOverdue"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Camt029_without_resolved_case_attaches_to_the_open_recall_and_otherwise_is_kept_for_operators()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R8");
        var recallId = (await Recall(harness, provider, Body("TX-R8")))["requestMessageId"]!.GetValue<string>();

        var fallback = await Resolution(provider, harness, GatewayRecallXml.Resolution("CT02-CXL-81", null, payment.MsgId!, "TX-R8", "E2E-TX-R8", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal((PapssCorrelation.TransactionId, PapssEventDisposition.Applied), (fallback.Correlation, fallback.Disposition));
        Assert.Equal(PapssOutcome.RecallRejectedByBeneficiary, (await StoredRecall(harness, recallId)).PapssOutcome);

        // No open recall any more (and an unknown RslvdCase): stored uncorrelated, acknowledged, visible to operators, not pushed.
        var orphan = await Resolution(provider, harness, GatewayRecallXml.Resolution("CT02-CXL-82", null, payment.MsgId!, "TX-R8", "E2E-TX-R8", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal((PapssEventDisposition.Uncorrelated, false, (PapssOperation?)null), (orphan.Disposition, orphan.Pushed, orphan.Operation));
        var unknownCase = await Resolution(provider, harness, GatewayRecallXml.Resolution("CT02-CXL-83", PapssFacingSipsClient.Id(), payment.MsgId!, "TX-R8", "E2E-TX-R8", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssEventDisposition.Uncorrelated, unknownCase.Disposition);
        // RslvdCase naming our recall but another transaction: not attached.
        var mismatch = await Resolution(provider, harness, GatewayRecallXml.Resolution("CT02-CXL-84", recallId, payment.MsgId!, "TX-OTHER", "E2E-TX-R8", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal((PapssCorrelation.Mismatch, PapssEventDisposition.Uncorrelated), (mismatch.Correlation, mismatch.Disposition));

        using var scope = provider.CreateScope();
        var unresolved = JsonNode.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(await Controller(harness, scope.ServiceProvider).UnresolvedPapssEvents(50, CancellationToken.None)).Value))!.AsArray();
        Assert.Equal(["CT02-CXL-84", "CT02-CXL-83", "CT02-CXL-82"], unresolved.Select(x => x!["sourceMessageId"]!.GetValue<string>()));
        Assert.All(unresolved, x => Assert.Equal(("RECALL_RESOLUTION", "camt.029.001.09", false), (x!["eventType"]!.GetValue<string>(), x["messageType"]!.GetValue<string>(), x["attached"]!.GetValue<bool>())));

        // An unknown recall id on a PAPSS pacs.002 is stored the same way, never attached to the payment.
        var status = await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-85", PapssFacingSipsClient.Id(), "TX-R8", "E2E-TX-R8", "RJCT", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal((PapssEventDisposition.Uncorrelated, (PapssOperation?)null), (status.Disposition, status.Operation));
        Assert.Equal(PapssOutcome.Settled, (await Stored(harness, "TX-R8")).PapssOutcome);
    }

    [Fact]
    public async Task Pacs004_after_a_recall_returns_the_payment_and_closes_the_recall()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R9");
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var bankCalls = harness.Bank.Count;
        var recallId = (await Recall(harness, provider, Body("TX-R9")))["requestMessageId"]!.GetValue<string>();
        await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-91", recallId, "TX-R9", "E2E-TX-R9", "ACCP", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssOutcome.Settled, (await Stored(harness, "TX-R9")).PapssOutcome); // ACCP never returns the payment

        var pacs004 = PostgresHarness.InboundReturn("CT02-RTN-91", "RTN-R9", "TX-R9", "E2E-TX-R9");
        var result = await InboundReturn(provider, harness, pacs004);
        Assert.Equal(PapssEventDisposition.Applied, result.Disposition);
        Assert.Contains(recallId, result.Note);
        Assert.Equal(PapssOutcome.Returned, (await Stored(harness, "TX-R9")).PapssOutcome);
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal((PapssOutcome.RecallReturned, "RTN-R9", "FOCR"), (recall.PapssOutcome, recall.ReturnId, recall.StatusReasonCode));
        Assert.NotNull(recall.CompletedAt);

        // Redelivered pacs.004: nothing new.
        Assert.True((await InboundReturn(provider, harness, pacs004)).Duplicate);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallReturned)));

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var calls = harness.Bank.Calls.Skip(bankCalls).ToList();
        Assert.Equal(3, calls.Count); // RECALL_ACCEPTED_BY_PAPSS, the return (CB_ReturnRequest) and RECALL_RETURNED
        Assert.Single(calls, c => c.Body.ContainsKey("returnId") && Text(c.Body, "returnId") == "RTN-R9");
        var returned = Assert.Single(calls, c => c.Body.ContainsKey("outcome") && Text(c.Body, "outcome") == "RECALL_RETURNED");
        Assert.Equal((recallId, "RTN-R9", "FOCR"), (Text(returned.Body, "recallId"), Text(returned.Body, "responderId"), Text(returned.Body, "reasonCode")));

        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-R9", null, CancellationToken.None));
        Assert.Equal("RETURNED", Text(lookup, "paymentOutcome"));
        var summary = Assert.Single(lookup["recalls"]!.AsArray())!;
        Assert.Equal(("COMPLETED", "RECALL_RETURNED", "RTN-R9"), (Text(summary, "status"), Text(summary, "papssOutcome"), Text(summary, "returnId")));
        var view = await Lookup(harness, provider, c => c.GetOperation(recallId, null, CancellationToken.None));
        Assert.Equal(("RECALL", "COMPLETED", "RETURNED"), (Text(view, "operation"), Text(view, "status"), Text(view, "originalPaymentOutcome")));
    }

    [Fact]
    public async Task Returned_amount_differing_within_the_exact_unwind_window_is_noted_and_a_return_without_recall_stays_spontaneous()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R10");
        var recallId = (await Recall(harness, provider, Body("TX-R10")))["requestMessageId"]!.GetValue<string>();
        harness.Clock.Advance(TimeSpan.FromDays(2));
        var result = await InboundReturn(provider, harness, PostgresHarness.InboundReturn("CT02-RTN-101", "RTN-R10", "TX-R10", "E2E-TX-R10", amount: 8m));
        Assert.Equal(PapssEventDisposition.Applied, result.Disposition);
        var recallEvent = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.EventType == PapssEventTypes.RecallReturned));
        Assert.Contains("differs from the original 10", recallEvent.Note);
        Assert.Contains("within 7 days", recallEvent.Note);
        Assert.Equal(PapssOutcome.RecallReturned, (await StoredRecall(harness, recallId)).PapssOutcome);

        // No recall: a spontaneous return behaves as before and creates no recall event.
        await SettledPayment(harness, provider, "TX-R11");
        var spontaneous = await InboundReturn(provider, harness, PostgresHarness.InboundReturn("CT02-RTN-111", "RTN-R11", "TX-R11", "E2E-TX-R11"));
        Assert.Equal(PapssEventDisposition.Applied, spontaneous.Disposition);
        Assert.Null(spontaneous.Note);
        Assert.Equal(PapssOutcome.Returned, (await Stored(harness, "TX-R11")).PapssOutcome);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallReturned)));
    }

    [Fact]
    public async Task Duplicate_recall_status_is_stored_once_and_pushed_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R12");
        var recallId = (await Recall(harness, provider, Body("TX-R12")))["requestMessageId"]!.GetValue<string>();
        var xml = GatewayRecallXml.RecallStatus("PAPSS-RS-121", recallId, "TX-R12", "E2E-TX-R12", "RJCT", reason: "1017", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic);
        Assert.False((await StatusCallback(provider, harness, xml)).Duplicate);
        Assert.True((await StatusCallback(provider, harness, xml)).Duplicate);
        // The same final answer under a new source id: history only.
        Assert.Equal(PapssEventDisposition.DuplicateFinal, (await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-122", recallId, "TX-R12", "E2E-TX-R12", "RJCT", reason: "1017", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic))).Disposition);
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Single(harness.Bank.Calls, c => c.Body.ContainsKey("recallId"));
        Assert.Equal(2, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallStatus)));
    }

    [Fact]
    public async Task Pending_recall_push_completes_exactly_once_after_a_restart()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        string recallId;
        await using (var first = harness.BuildProvider())
        {
            await SettledPayment(harness, first, "TX-R13");
            await first.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
            recallId = (await Recall(harness, first, Body("TX-R13")))["requestMessageId"]!.GetValue<string>();
            harness.Bank.Status = System.Net.HttpStatusCode.ServiceUnavailable;
            await StatusCallback(first, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-131", recallId, "TX-R13", "E2E-TX-R13", "ACCP", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
            await first.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        }
        var failedAttempts = harness.Bank.Calls.Count(c => c.Body.ContainsKey("recallId"));
        Assert.Equal(1, failedAttempts);

        harness.Bank.Status = System.Net.HttpStatusCode.OK;
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await using var second = harness.BuildProvider();
        await second.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        await second.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Bank.Calls.Count(c => c.Body.ContainsKey("recallId")));
        var e = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.SourceMessageId == "PAPSS-RS-131"));
        Assert.Equal((PapssDeliveryState.Delivered, 2), (e.PushState, e.PushAttempts));
        Assert.Equal(PapssDeliveryState.Delivered, (await StoredRecall(harness, recallId)).BankDeliveryState);
    }

    [Fact]
    public async Task Recall_lookups_show_the_linked_state_and_flag_an_overdue_answer()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R14");
        var recallId = (await Recall(harness, provider, Body("TX-R14")))["requestMessageId"]!.GetValue<string>();

        var byId = await Lookup(harness, provider, c => c.GetRecall(recallId, null, CancellationToken.None));
        Assert.Equal((recallId, "RECALL", "OUTBOUND", "PENDING", "RECALL_PENDING", "ADMITTED"),
            (Text(byId, "requestMessageId"), Text(byId, "operation"), Text(byId, "direction"), Text(byId, "status"), Text(byId, "papssOutcome"), Text(byId, "gatewayState")));
        Assert.Equal((payment.RequestMessageId, "TX-R14", "E2E-TX-R14", "SETTLED"), (Text(byId, "originalRequestMessageId"), Text(byId, "originalTxId"), Text(byId, "originalEndToEndId"), Text(byId, "originalPaymentOutcome")));
        Assert.Equal("2026-10-25T08:00:00.000Z", Text(byId, "deadlineAt"));
        Assert.False(byId["responseOverdue"]!.GetValue<bool>());
        Assert.Equal(Text(byId, "requestMessageId"), Text(await Lookup(harness, provider, c => c.GetOperation(recallId, null, CancellationToken.None)), "requestMessageId"));

        var byEndToEnd = await Lookup(harness, provider, c => c.FindOperation("E2E-TX-R14", null, CancellationToken.None));
        Assert.Equal(("PAYMENT", recallId), (Text(byEndToEnd, "operation"), Text(Assert.Single(byEndToEnd["recalls"]!.AsArray())!, "recallId")));

        // Record-only deadline: past it the recall is flagged, never changed.
        harness.Clock.Advance(TimeSpan.FromDays(31));
        var overdue = await Lookup(harness, provider, c => c.GetRecall(recallId, null, CancellationToken.None));
        Assert.Equal(("PENDING", true), (Text(overdue, "status"), overdue["responseOverdue"]!.GetValue<bool>()));
        Assert.True(Assert.Single((await Lookup(harness, provider, c => c.GetPayment("TX-R14", null, CancellationToken.None)))["recalls"]!.AsArray())!["responseOverdue"]!.GetValue<bool>());
        Assert.Equal(PapssOutcome.RecallPending, (await StoredRecall(harness, recallId)).PapssOutcome);

        using var scope = provider.CreateScope();
        Assert.IsType<NotFoundObjectResult>(await Controller(harness, scope.ServiceProvider).GetRecall(PapssFacingSipsClient.Id(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Outbound_return_settles_on_accp_by_default()
    {
        // PAPSS-confirmed 2026-09-26: the returner's authoritative return status is ACCP.
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await RecordInbound(harness, provider, "CT02-IN-R15", "TX-R15IN", "E2E-TX-R15IN");
        using (var scope = provider.CreateScope())
        {
            var body = new JsonObject
            {
                ["rail"] = "PAPSS", ["toBIC"] = PostgresHarness.ForeignBic, ["lclInstrument"] = "USDP", ["ctgPurp"] = "CASH", ["originalAmount"] = 25, ["originalCurrency"] = "USD",
                ["txId"] = "TX-R15IN", ["endToEndId"] = "E2E-TX-R15IN", ["reason"] = "FOCR", ["additionalInfo"] = "customer request", ["returnId"] = "RTN-R15"
            };
            Assert.IsType<OkObjectResult>(await Controller(harness, scope.ServiceProvider).GetReturn(body, CancellationToken.None));
        }
        var ret = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.ReturnId == "RTN-R15"));
        var accp = await StatusCallback(provider, harness, PostgresHarness.StatusReport("PAPSS-S-151", ret.MsgId!, "pacs.004.001.11", "TX-R15IN", "E2E-TX-R15IN", "ACCP"));
        Assert.Equal(PapssEventDisposition.Applied, accp.Disposition);
        Assert.Equal(PapssOutcome.Settled, (await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == ret.Id))).PapssOutcome);
        Assert.Equal(PapssOutcome.Returned, (await Stored(harness, "TX-R15IN", PapssDirection.Inbound)).PapssOutcome);
    }

    // ---------------------------------------------------------------------------------------------
    // S3 recovery: RECALL_OUTCOME_UNRESOLVED and the manual operator close
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Recall_outcome_unresolved_keeps_the_recall_open_and_blocks_a_new_recall()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R17");
        var paymentBefore = Snapshot(await Stored(harness, "TX-R17"));
        var recallId = (await Recall(harness, provider, Body("TX-R17")))["requestMessageId"]!.GetValue<string>();

        // Gateway-confirmed real wire shape: TxSts=PDNG with StsRsnInf/Rsn/Prtry=RECALL_OUTCOME_UNRESOLVED. The ambiguous signal
        // is carried in the reason (ISO's TxSts is capped at 4 characters), so SIPS Connect never hard-requires a particular TxSts.
        var xml = GatewayRecallXml.RecallStatus("PAPSS-RS-171", recallId, "TX-R17", "E2E-TX-R17", "PDNG", reason: PapssRecallMessages.UnresolvedStatus, from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic);
        var result = await StatusCallback(provider, harness, xml);
        Assert.Equal((PapssCorrelation.MessageId, PapssEventDisposition.Applied, true), (result.Correlation, result.Disposition, result.Pushed));
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal(PapssOutcome.RecallOutcomeUnresolved, recall.PapssOutcome);
        Assert.Null(recall.CompletedAt); // still OPEN, not final
        Assert.Equal(PapssGatewayState.Admitted, recall.GatewayState);

        // Blocks a new recall of the same payment, same as RECALL_PENDING/RECALL_ACCEPTED_BY_PAPSS would.
        Assert.Equal((409, "RECALL_ALREADY_OPEN"), await Refusal(harness, provider, Body("TX-R17")));
        // The payment itself is completely untouched (isolation holds for this new outcome too).
        Assert.Equal(paymentBefore, Snapshot(await Stored(harness, "TX-R17")));

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var push = Assert.Single(harness.Bank.Calls, c => c.Body.ContainsKey("recallId"));
        Assert.Equal(("RECALL_OUTCOME_UNRESOLVED", recallId), (Text(push.Body, "outcome"), Text(push.Body, "recallId")));

        var lookup = await Lookup(harness, provider, c => c.GetRecall(recallId, null, CancellationToken.None));
        Assert.Equal(("UNKNOWN", "RECALL_OUTCOME_UNRESOLVED"), (Text(lookup, "status"), Text(lookup, "papssOutcome")));
    }

    [Fact]
    public async Task A_later_camt029_or_pacs004_still_resolves_an_unresolved_recall_without_operator_action()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R18");
        var recallId = (await Recall(harness, provider, Body("TX-R18")))["requestMessageId"]!.GetValue<string>();
        await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-181", recallId, "TX-R18", "E2E-TX-R18", "PDNG", reason: PapssRecallMessages.UnresolvedStatus, from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssOutcome.RecallOutcomeUnresolved, (await StoredRecall(harness, recallId)).PapssOutcome);

        var camt029 = GatewayRecallXml.Resolution("CT02-CXL-181", recallId, payment.MsgId!, "TX-R18", "E2E-TX-R18", reason: "AGNT", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic);
        var resolution = await Resolution(provider, harness, camt029);
        Assert.Equal(PapssEventDisposition.Applied, resolution.Disposition);
        Assert.Equal(PapssOutcome.RecallRejectedByBeneficiary, (await StoredRecall(harness, recallId)).PapssOutcome);
        Assert.NotNull((await StoredRecall(harness, recallId)).CompletedAt);
        // A new recall is now allowed (the earlier one resolved to a final state on its own, no manual close needed).
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await Recall(harness, provider, Body("TX-R18", reason: "FRAD")))["requestMessageId"]!.GetValue<string>();
        Assert.NotEqual(recallId, second);
    }

    [Fact]
    public async Task Operator_closes_an_unresolved_recall_and_a_new_recall_is_then_allowed()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R19");
        var paymentBefore = Snapshot(await Stored(harness, "TX-R19"));
        var recallId = (await Recall(harness, provider, Body("TX-R19")))["requestMessageId"]!.GetValue<string>();
        await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-191", recallId, "TX-R19", "E2E-TX-R19", "PDNG", reason: PapssRecallMessages.UnresolvedStatus, from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssOutcome.RecallOutcomeUnresolved, (await StoredRecall(harness, recallId)).PapssOutcome);

        var closed = await CloseRecall(harness, provider, recallId, "gateway reported RECALL_OUTCOME_UNRESOLVED; confirmed with PAPSS portal that no funds moved after 45 days", "ops.alice");
        Assert.Equal(("RECALL", "REJECTED", "RECALL_ABANDONED"), (Text(closed, "operation"), Text(closed, "status"), Text(closed, "papssOutcome")));
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal(PapssOutcome.RecallAbandoned, recall.PapssOutcome);
        Assert.NotNull(recall.CompletedAt);
        Assert.Equal(("OPERATOR_CLOSE", "gateway reported RECALL_OUTCOME_UNRESOLVED; confirmed with PAPSS portal that no funds moved after 45 days"), (recall.StatusReasonCode, recall.AdditionalInfo));

        // Audit event: who, when, why - reusing the existing event log, never applied automatically.
        var auditEvent = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.EventType == PapssEventTypes.RecallClosed && x.OperationId == recall.Id));
        var audit = PapssRecallMessages.ParseCloseAudit(auditEvent.RawXml);
        Assert.Equal("ops.alice", audit.ClosedBy);
        Assert.Contains("RECALL_OUTCOME_UNRESOLVED", audit.Reason);
        Assert.Equal(harness.Clock.GetUtcNow(), audit.ClosedAt);
        Assert.Contains("ops.alice", auditEvent.Note);
        Assert.Contains("RECALL_OUTCOME_UNRESOLVED", auditEvent.Note); // records the previous outcome too

        // Isolation holds: the recalled payment is completely untouched by the manual close.
        Assert.Equal(paymentBefore, Snapshot(await Stored(harness, "TX-R19")));

        // Releases the one-open-recall lock: a new recall of the same payment now succeeds.
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await Recall(harness, provider, Body("TX-R19", reason: "FRAD")))["requestMessageId"]!.GetValue<string>();
        Assert.NotEqual(recallId, second);
        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-R19", null, CancellationToken.None));
        Assert.Equal([(recallId, "REJECTED", "RECALL_ABANDONED"), (second, "PENDING", "RECALL_PENDING")],
            lookup["recalls"]!.AsArray().Select(r => (Text(r!, "recallId"), Text(r!, "status"), Text(r!, "papssOutcome"))));

        // Pushed to the bank like any other recall answer (in addition to the earlier RECALL_OUTCOME_UNRESOLVED push).
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var pushes = harness.Bank.Calls.Where(c => c.Body.ContainsKey("recallId") && Text(c.Body, "recallId") == recallId).ToList();
        Assert.Equal(["RECALL_OUTCOME_UNRESOLVED", "RECALL_ABANDONED"], pushes.Select(c => Text(c.Body, "outcome")));
        Assert.Equal("ops.alice", Text(pushes[^1].Body, "responderId"));
    }

    /// <summary>
    /// The second of the three required authorization cases, exercised through the full ingestion+audit pipeline
    /// (not just the authorization-decision logic, which the unit-test theory already covers): an API party
    /// authenticated via ApiKeyDefaults.AuthenticationScheme and granted only the narrow KnownRoles.RecallClose
    /// capability (on top of the ordinary Gateway baseline every API key gets - never the human Recon role) can
    /// close a recall stuck OPEN, and the resulting RECALL_CLOSED audit event records API_PARTY as the auth path.
    /// </summary>
    [Fact]
    public async Task Api_party_with_the_narrow_capability_closes_an_unresolved_recall()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R19B");
        var recallId = (await Recall(harness, provider, Body("TX-R19B")))["requestMessageId"]!.GetValue<string>();
        await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-19B", recallId, "TX-R19B", "E2E-TX-R19B", "PDNG", reason: PapssRecallMessages.UnresolvedStatus, from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssOutcome.RecallOutcomeUnresolved, (await StoredRecall(harness, recallId)).PapssOutcome);

        var closed = await CloseRecall(harness, provider, recallId, "automated closure per playbook after 45 days", "svc.recall-uat", asApiParty: true);
        Assert.Equal(("RECALL", "REJECTED", "RECALL_ABANDONED"), (Text(closed, "operation"), Text(closed, "status"), Text(closed, "papssOutcome")));
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal(PapssOutcome.RecallAbandoned, recall.PapssOutcome);

        var auditEvent = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.EventType == PapssEventTypes.RecallClosed && x.OperationId == recall.Id));
        var audit = PapssRecallMessages.ParseCloseAudit(auditEvent.RawXml);
        Assert.Equal("svc.recall-uat", audit.ClosedBy);
        Assert.Equal(PapssRecallCloseAuthPath.ApiParty, audit.AuthPath);
        Assert.Contains("svc.recall-uat", auditEvent.Note);
        Assert.Contains(PapssRecallCloseAuthPath.ApiParty, auditEvent.Note);

        // Releases the one-open-recall lock just like the operator path does.
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await Recall(harness, provider, Body("TX-R19B", reason: "FRAD")))["requestMessageId"]!.GetValue<string>();
        Assert.NotEqual(recallId, second);
    }

    [Fact]
    public async Task Close_recall_refuses_unknown_missing_reason_and_an_already_closed_recall()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SettledPayment(harness, provider, "TX-R20");
        var recallId = (await Recall(harness, provider, Body("TX-R20")))["requestMessageId"]!.GetValue<string>();

        // Unknown recall id.
        Assert.Equal((404, "OPERATION_NOT_FOUND"), await CloseRecallRefusal(harness, provider, PapssFacingSipsClient.Id(), "some reason"));
        // Missing / blank reason.
        Assert.Equal((422, "PAPSS_VALIDATION_FAILED"), await CloseRecallRefusal(harness, provider, recallId, null));
        Assert.Equal((422, "PAPSS_VALIDATION_FAILED"), await CloseRecallRefusal(harness, provider, recallId, "   "));
        // Nothing was written by any of the refused attempts.
        Assert.Equal(PapssOutcome.RecallPending, (await StoredRecall(harness, recallId)).PapssOutcome);
        Assert.Equal(0, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallClosed)));

        // First close succeeds...
        await CloseRecall(harness, provider, recallId, "operator decision after manual review");
        Assert.Equal(PapssOutcome.RecallAbandoned, (await StoredRecall(harness, recallId)).PapssOutcome);
        // ...a second close of the same (already-closed) recall is refused, and nothing changes.
        Assert.Equal((409, "RECALL_ALREADY_CLOSED"), await CloseRecallRefusal(harness, provider, recallId, "trying again"));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallClosed)));

        // Closing a recall that is already final for an ordinary reason (PAPSS rejected it) is refused the same way.
        var second = (await Recall(harness, provider, Body("TX-R20", reason: "FRAD")))["requestMessageId"]!.GetValue<string>();
        await StatusCallback(provider, harness, GatewayRecallXml.RecallStatus("PAPSS-RS-201", second, "TX-R20", "E2E-TX-R20", "RJCT", from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic));
        Assert.Equal(PapssOutcome.RecallRejectedByPapss, (await StoredRecall(harness, second)).PapssOutcome);
        Assert.Equal((409, "RECALL_ALREADY_CLOSED"), await CloseRecallRefusal(harness, provider, second, "not actually stuck"));
    }

    [Fact]
    public async Task Close_recall_concurrent_double_close_only_applies_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await SettledPayment(harness, provider, "TX-R21");
        var recallId = (await Recall(harness, provider, Body("TX-R21")))["requestMessageId"]!.GetValue<string>();

        // Two concurrent close attempts on the same still-open recall: the deterministic audit-event id (keyed on the recall,
        // not a random guid) means only one can win at the database level even if both pass the application-level open check.
        var first = CloseRecallRaw(harness, provider, recallId, "attempt A", "ops.a");
        var second = CloseRecallRaw(harness, provider, recallId, "attempt B", "ops.b");
        await Task.WhenAll(first, second);
        var results = new[] { await first, await second };
        Assert.Single(results, r => r is OkObjectResult);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallClosed)));
        Assert.Equal(PapssOutcome.RecallAbandoned, (await StoredRecall(harness, recallId)).PapssOutcome);
    }

    /// <summary>
    /// The literal, unmodified bytes the Gateway POSTed for the S3 RECALL_OUTCOME_UNRESOLVED signal (real, gateway-synthesized
    /// pacs.002.001.12; not hand-written). See art/papss/evidence/recall-outcome-unresolved-callback-20260926.md. Fed through
    /// the same real ingestion path production uses (parsing, correlation by OrgnlMsgId+OrgnlMsgNmId, outcome resolution),
    /// with a payment and recall set up to exactly match the fixture's own ids (TxId=TX-OUT-1, EndToEndId=E2E-OUT-1,
    /// recallId=SIPS-0123456789abcdef0123cccc). Cryptographic signature verification of this exact fixture is covered
    /// separately (SIPS.Connect.Tests.PapssRecallCapturedFixtureTests): we do not have the Gateway's ephemeral test
    /// certificate, so this test exercises everything below the signature check, as the store already does once
    /// PapssCallbackGuard has admitted a callback.
    /// </summary>
    [Fact]
    public async Task Real_gateway_captured_unresolved_callback_is_recognized_end_to_end()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        const string recallId = "SIPS-0123456789abcdef0123cccc";

        // A settled OUTBOUND payment matching the fixture's own ids exactly.
        using (var scope = provider.CreateScope())
        {
            var body = new JsonObject
            {
                ["rail"] = "PAPSS", ["agent"] = PostgresHarness.ForeignBic, ["lclInstrument"] = "USDP", ["ctgPurp"] = "CASH",
                ["localId"] = "E2E-OUT-1", ["txId"] = "TX-OUT-1", ["amount"] = 10, ["currency"] = "USD",
                ["drName"] = "AMINA ALI", ["drAccount"] = "100200", ["drAccountType"] = "BBAN",
                ["crName"] = "FORTRESS GLOBAL", ["crAccount"] = "0012030321735", ["crAccountType"] = "BBAN", ["crAgentBIC"] = PostgresHarness.ForeignBic, ["narration"] = "qualification"
            };
            Assert.IsType<OkObjectResult>(await Controller(harness, scope.ServiceProvider).MakePayment(body, CancellationToken.None));
        }
        var payment = await Stored(harness, "TX-OUT-1");
        await StatusCallback(provider, harness, PostgresHarness.StatusReport("PAPSS-S-TX-OUT-1", payment.MsgId!, "pacs.008.001.10", "TX-OUT-1", "E2E-OUT-1", "ACSC"));
        Assert.Equal(PapssOutcome.Settled, (await Stored(harness, "TX-OUT-1")).PapssOutcome);

        var admission = await Recall(harness, provider, Body("TX-OUT-1", recallId: recallId));
        Assert.Equal(recallId, admission["requestMessageId"]!.GetValue<string>());

        // 1 (signature) is covered by SIPS.Connect.Tests.PapssRecallCapturedFixtureTests; feed the exact captured bytes
        // through the real store ingestion path here (2: correlation, 3: outcome + evidence).
        var xml = LoadCapturedUnresolvedFixture();
        var first = await StatusCallback(provider, harness, xml);
        Assert.Equal((PapssCorrelation.MessageId, PapssEventDisposition.Applied, true), (first.Correlation, first.Disposition, first.Pushed));
        var recall = await StoredRecall(harness, recallId);
        Assert.Equal((PapssOutcome.RecallOutcomeUnresolved, "PDNG", "RECALL_OUTCOME_UNRESOLVED"), (recall.PapssOutcome, recall.PaymentStatus, recall.StatusReasonCode));
        Assert.Null(recall.CompletedAt); // still OPEN

        // 3: evidence visible via GET Recall/{id}, including the gateway's own raw-evidence hash reference.
        var lookup = await Lookup(harness, provider, c => c.GetRecall(recallId, null, CancellationToken.None));
        Assert.Equal(("RECALL", "UNKNOWN", "RECALL_OUTCOME_UNRESOLVED", "RECALL_OUTCOME_UNRESOLVED"), (Text(lookup, "operation"), Text(lookup, "status"), Text(lookup, "papssOutcome"), Text(lookup, "statusReasonCode")));
        Assert.False(string.IsNullOrWhiteSpace(Text(lookup, "additionalInfo"))); // free text varies by failure mode; only non-empty is asserted
        var history = Assert.Single(lookup["statusHistory"]!.AsArray())!;
        Assert.Equal("1e16475886d80cd551aa9c3da532abaa926b5d9bfc80c3ed965a03624cd6e456", Text(history, "rawEvidenceReference"));
        Assert.Equal("b4a8d1ef005c84998d6b1e504f0b8d1a", Text(history, "sourceMessageId"));

        // 5: idempotent redelivery of the exact same byte-for-byte payload does not create a duplicate event or change state
        // (the Gateway confirmed BizMsgIdr/effect id/hash are deterministic per recall, not per retry).
        var eventsBefore = await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallStatus && x.OperationId == recall.Id));
        var redelivered = await StatusCallback(provider, harness, xml);
        Assert.True(redelivered.Duplicate);
        Assert.Equal(eventsBefore, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.RecallStatus && x.OperationId == recall.Id)));
        Assert.Equal(PapssOutcome.RecallOutcomeUnresolved, (await StoredRecall(harness, recallId)).PapssOutcome);

        // Still OPEN: blocks a new recall of the same payment.
        Assert.Equal((409, "RECALL_ALREADY_OPEN"), await Refusal(harness, provider, Body("TX-OUT-1", reason: "DUPL")));
        // The recalled payment itself was never touched by any of this.
        Assert.Equal(PapssOutcome.Settled, (await Stored(harness, "TX-OUT-1")).PapssOutcome);

        // 4: operator close releases the one-open-recall lock.
        await CloseRecall(harness, provider, recallId, "gateway-confirmed RECALL_OUTCOME_UNRESOLVED; PAPSS side manually reconciled, no funds moved");
        Assert.Equal(PapssOutcome.RecallAbandoned, (await StoredRecall(harness, recallId)).PapssOutcome);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await Recall(harness, provider, Body("TX-OUT-1", reason: "FRAD")))["requestMessageId"]!.GetValue<string>();
        Assert.NotEqual(recallId, second);
    }

    /// <summary>Loads the exact fixture bytes copied into this project (see the csproj comment) and re-checks the documented hash.</summary>
    private static string LoadCapturedUnresolvedFixture()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "recall-outcome-unresolved-callback-20260926.xml"));
        Assert.Equal("aede643226bbc9b3cc6213c5a61ab1abdaf141e04619b7ac01862916edda4660", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant());
        return xml;
    }

    // ---------------------------------------------------------------------------------------------

    private static JsonObject Payment(string txId) => new()
    {
        ["rail"] = "PAPSS", ["agent"] = PostgresHarness.ForeignBic, ["lclInstrument"] = "USDP", ["ctgPurp"] = "CASH", ["localId"] = "E2E-" + txId, ["txId"] = txId,
        ["amount"] = 10, ["currency"] = "USD", ["drName"] = "AMINA ALI", ["drAccount"] = "100200", ["drAccountType"] = "BBAN",
        ["crName"] = "FORTRESS GLOBAL", ["crAccount"] = "0012030321735", ["crAccountType"] = "BBAN", ["crAgentBIC"] = PostgresHarness.ForeignBic, ["narration"] = "invoice 7"
    };

    private static JsonObject Body(string txId, string reason = "DUPL", string? recallId = null)
    {
        var body = new JsonObject { ["rail"] = "PAPSS", ["txId"] = txId, ["reason"] = reason };
        if (recallId is not null) body["recallId"] = recallId;
        return body;
    }

    private static GatewayController Controller(PostgresHarness harness, IServiceProvider services)
    {
        var router = new Mock<IParticipantOperationRouter>();
        router.Setup(x => x.Select(It.IsAny<ParticipantOperation>(), It.IsAny<string?>())).Returns(DownstreamRail.Papss);
        router.Setup(x => x.ResolvePapss()).Returns(harness.Binding());
        return new GatewayController(
            services.GetRequiredService<IJsonAdapter>(),
            Mock.Of<IOutgoingVerificationHandler>(), Mock.Of<IOutgoingTransactionHandler>(), Mock.Of<IOutgoingTransactionStatusHandler>(),
            Mock.Of<IOutgoingReturnTransactionHandler>(), Mock.Of<IReturnRetryHandler>(), Microsoft.Extensions.Options.Options.Create(new CoreOptions()),
            router.Object, services.GetRequiredService<IPapssFacingSipsClient>(), services.GetRequiredService<PapssOperationStore>(),
            harness.Options, harness.Clock, services.GetRequiredService<IPapssPaymentService>());
    }

    private static async Task<JsonObject> Pay(PostgresHarness harness, ServiceProvider provider, JsonObject body)
    {
        using var scope = provider.CreateScope();
        return (JsonObject)Assert.IsType<OkObjectResult>(await Controller(harness, scope.ServiceProvider).MakePayment(body, CancellationToken.None)).Value!;
    }

    /// <summary>An OUTBOUND payment admitted and then settled by PAPSS (pacs.002 ACSC).</summary>
    private static async Task<PapssOperation> SettledPayment(PostgresHarness harness, ServiceProvider provider, string txId)
    {
        await Pay(harness, provider, Payment(txId));
        var payment = await Stored(harness, txId);
        await StatusCallback(provider, harness, PostgresHarness.StatusReport("PAPSS-S-" + txId, payment.MsgId!, "pacs.008.001.10", txId, "E2E-" + txId, "ACSC"));
        var settled = await Stored(harness, txId);
        Assert.Equal(PapssOutcome.Settled, settled.PapssOutcome);
        return settled;
    }

    private static async Task<JsonObject> Recall(PostgresHarness harness, ServiceProvider provider, JsonObject body)
    {
        using var scope = provider.CreateScope();
        var result = await Controller(harness, scope.ServiceProvider).Recall(body, CancellationToken.None);
        return (JsonObject)Assert.IsType<OkObjectResult>(result).Value!;
    }

    private static async Task<(int Status, string Code)> Refusal(PostgresHarness harness, ServiceProvider provider, JsonObject body)
    {
        using var scope = provider.CreateScope();
        var result = Assert.IsAssignableFrom<ObjectResult>(await Controller(harness, scope.ServiceProvider).Recall(body, CancellationToken.None));
        var json = JsonNode.Parse(JsonSerializer.Serialize(result.Value))!;
        return (result.StatusCode ?? 200, json["code"]!.GetValue<string>());
    }

    /// <summary>
    /// Calls POST Recall/{recallId}/Close as the named operator (default "ops.test"; the store records this as closedBy).
    /// Pass <paramref name="asApiParty"/> true to authenticate the call the way a real API party would: an
    /// ApiKeyDefaults.AuthenticationScheme identity carrying the narrow KnownRoles.RecallClose claim (in addition to
    /// the ordinary Gateway baseline every API key gets), so the controller's own auth-path detection resolves this
    /// to PapssRecallCloseAuthPath.ApiParty end-to-end through the real ingestion+audit pipeline.
    /// </summary>
    private static async Task<ActionResult> CloseRecallRaw(PostgresHarness harness, ServiceProvider provider, string recallId, string? reason, string closedBy = "ops.test", bool asApiParty = false)
    {
        using var scope = provider.CreateScope();
        var controller = Controller(harness, scope.ServiceProvider);
        var identity = asApiParty
            ? new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, closedBy),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, SIPS.Connect.KnownRoles.Gateway),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, SIPS.Connect.KnownRoles.RecallClose)
                ], SIPS.Connect.Services.ApiKeyDefaults.AuthenticationScheme)
            : new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, closedBy)], "Test");
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(identity)
            }
        };
        return await controller.CloseRecall(recallId, new PapssRecallCloseRequest { Reason = reason }, CancellationToken.None);
    }

    private static async Task<JsonObject> CloseRecall(PostgresHarness harness, ServiceProvider provider, string recallId, string reason, string closedBy = "ops.test", bool asApiParty = false)
        => (JsonObject)Assert.IsType<OkObjectResult>(await CloseRecallRaw(harness, provider, recallId, reason, closedBy, asApiParty)).Value!;

    private static async Task<(int Status, string Code)> CloseRecallRefusal(PostgresHarness harness, ServiceProvider provider, string recallId, string? reason, string closedBy = "ops.test")
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(await CloseRecallRaw(harness, provider, recallId, reason, closedBy));
        var json = JsonNode.Parse(JsonSerializer.Serialize(result.Value))!;
        return (result.StatusCode ?? 200, json["code"]!.GetValue<string>());
    }

    private static async Task<string> RefusalMessage(PostgresHarness harness, ServiceProvider provider, JsonObject body)
    {
        using var scope = provider.CreateScope();
        var result = Assert.IsAssignableFrom<ObjectResult>(await Controller(harness, scope.ServiceProvider).Recall(body, CancellationToken.None));
        return JsonNode.Parse(JsonSerializer.Serialize(result.Value))!["message"]!.GetValue<string>();
    }

    private static async Task<JsonObject> Lookup(PostgresHarness harness, ServiceProvider provider, Func<GatewayController, Task<ActionResult>> call)
    {
        using var scope = provider.CreateScope();
        return (JsonObject)Assert.IsType<OkObjectResult>(await call(Controller(harness, scope.ServiceProvider))).Value!;
    }

    private static async Task<PapssIngestResult> StatusCallback(ServiceProvider provider, PostgresHarness harness, string pacs002)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>().HandleStatusReportAsync(harness.Binding(), pacs002, CancellationToken.None);
    }

    private static async Task<PapssIngestResult> Resolution(ServiceProvider provider, PostgresHarness harness, string camt029)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>().HandleRecallResolutionAsync(harness.Binding(), camt029, CancellationToken.None);
    }

    private static async Task<PapssIngestResult> InboundReturn(ServiceProvider provider, PostgresHarness harness, string pacs004)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>().HandleReturnAsync(harness.Binding(), pacs004, CancellationToken.None);
    }

    /// <summary>A received payment (pacs.008) with its bank decision, as the legacy handler records it.</summary>
    private static async Task RecordInbound(PostgresHarness harness, ServiceProvider provider, string sourceId, string txId, string endToEndId)
    {
        var pacs008 = PostgresHarness.InboundPayment(sourceId, txId, endToEndId);
        await harness.WithStorageAsync(async db =>
        {
            db.ISOMessages.Add(new ISOMessage
            {
                MessageType = ISOMessageType.TransactionRequest, Status = TransactionStatus.Success, MsgId = "MSG-" + txId, BizMsgIdr = sourceId, MsgDefIdr = "pacs.008.001.10",
                BusinessService = harness.Options.SecurityProfile, TxId = txId, EndToEndId = endToEndId, Date = DateTimeOffset.UtcNow, FromBIC = PostgresHarness.Gateway, ToBIC = PostgresHarness.LocalBic,
                Message = Encoding.UTF8.GetBytes(pacs008), Response = Encoding.UTF8.GetBytes(PostgresHarness.Decision(txId, endToEndId, "ACCP")),
                PapssDecision = Encoding.UTF8.GetBytes("<signed-decision/>")
            });
            return await db.SaveChangesAsync(CancellationToken.None);
        });
        using var scope = provider.CreateScope();
        var callbacks = scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>();
        await callbacks.RecordInboundPaymentAsync(harness.Binding(), pacs008, CancellationToken.None);
        await callbacks.SyncInboundPaymentAsync(pacs008, CancellationToken.None);
    }

    private static Task<PapssOperation> Stored(PostgresHarness harness, string txId, PapssDirection direction = PapssDirection.Outbound)
        => harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Operation == PapssOperationType.Payment && x.Direction == direction && x.TxId == txId));

    private static Task<PapssOperation> StoredRecall(PostgresHarness harness, string recallId)
        => harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Operation == PapssOperationType.Recall && x.RequestMessageId == recallId));

    /// <summary>Every state column of the payment: the isolation tests require it to be unchanged byte for byte.</summary>
    private static string Snapshot(PapssOperation x)
        => string.Join('|', x.PapssOutcome, x.PaymentStatus, x.StatusReasonCode, x.StatusAt?.ToUnixTimeMilliseconds(), x.CompletedAt?.ToUnixTimeMilliseconds(), x.UpdatedAt.ToUnixTimeMilliseconds(),
            x.GatewayState, x.BankDeliveryState, x.StatusConflict, x.AdditionalInfo, x.Amount, x.Currency, x.xmin);

    private static Task<string> PaymentEvents(PostgresHarness harness, Guid paymentId)
        => harness.WithStorageAsync(async db => string.Join(';', (await db.PapssOperationEvents.AsNoTracking().Where(x => x.OperationId == paymentId).OrderBy(x => x.Id).ToListAsync())
            .Select(x => $"{x.Id}:{x.EventType}:{x.SourceMessageId}:{x.Status}:{x.Disposition}")));

    private static string Text(JsonNode node, string name) => node[name]!.GetValue<string>();
}
