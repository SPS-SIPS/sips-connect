using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Services;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Connect.PostgresTests;

public sealed class PapssPaymentDecisionPublisherTests
{
    [Theory]
    [InlineData("ACCP")]
    [InlineData("RJCT")]
    public async Task Explicit_bank_decision_is_persisted_and_published_once(string status)
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var inbound = await RecordAsync(storage, harness, status, TransactionStatus.Pending);
        var gateway = new Mock<IPapssFacingSipsClient>();
        string? sent = null;
        gateway.Setup(g => g.SubmitPaymentDecisionAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((PapssParticipantBinding participant, string xml, CancellationToken ct) => sent = xml)
            .ReturnsAsync(new PapssAdmissionResponse("DECISION", "RECEIVED_AND_DURABLY_ADMITTED", true));
        var publisher = Publisher(storage, harness, gateway.Object);

        await publisher.PersistAndSubmitAsync(harness.Binding(), inbound, CancellationToken.None);
        await publisher.PersistAndSubmitAsync(harness.Binding(), inbound, CancellationToken.None);

        var record = await storage.ISOMessages.AsNoTracking().SingleAsync();
        Assert.NotNull(record.PapssDecisionPublishedAt);
        Assert.Equal(sent, Encoding.UTF8.GetString(record.PapssDecision!));
        Assert.Equal(status, PapssPaymentMessages.DecisionStatus(sent!).Status);
        if (status == "RJCT") Assert.Equal("AM04", PapssPaymentMessages.DecisionStatus(sent!).Reason);
        gateway.Verify(g => g.SubmitPaymentDecisionAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ACSC")]
    [InlineData("PDNG")]
    public async Task Previously_queued_default_decision_cannot_be_submitted_without_bank_acceptance(string status)
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var inbound = await RecordAsync(storage, harness, status, TransactionStatus.Pending, queued: true);
        var gateway = new Mock<IPapssFacingSipsClient>(MockBehavior.Strict);
        var publisher = Publisher(storage, harness, gateway.Object);

        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PersistAndSubmitAsync(harness.Binding(), inbound, CancellationToken.None));
        Assert.Null((await storage.ISOMessages.AsNoTracking().SingleAsync()).PapssDecisionPublishedAt);
        gateway.VerifyNoOtherCalls();
    }

    /// <summary>
    /// TVR UAT 2026-10-10: PAPSS re-signs every physical retry of the same TxId with a fresh envelope
    /// BizMsgIdr/CreDt. The stored record.Message always holds whatever was captured on the first attempt, so
    /// comparing a retry's raw bytes against it byte-for-byte never matched - rejecting every legitimate retry
    /// as DUPLICATE_CONFLICT and never publishing a decision (observed live: 20261010DJ10090644291791614669253so,
    /// looping forever instead of ever resolving).
    /// </summary>
    [Fact]
    public async Task Retry_with_a_freshly_resigned_envelope_still_matches_the_stored_payment()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var inboundFirst = await RecordAsync(storage, harness, "ACCP", TransactionStatus.Pending);
        var gateway = new Mock<IPapssFacingSipsClient>();
        gateway.Setup(g => g.SubmitPaymentDecisionAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PapssAdmissionResponse("DECISION", "RECEIVED_AND_DURABLY_ADMITTED", true));
        var publisher = Publisher(storage, harness, gateway.Object);

        var retry = PostgresHarness.SetHeader(PostgresHarness.SetHeader(inboundFirst, "BizMsgIdr", "PAPSS-RETRY-2"), "CreDt", DateTime.UtcNow.AddMinutes(5).ToString("o"));

        await publisher.PersistAndSubmitAsync(harness.Binding(), inboundFirst, CancellationToken.None);
        await publisher.PersistAndSubmitAsync(harness.Binding(), retry, CancellationToken.None);

        Assert.NotNull((await storage.ISOMessages.AsNoTracking().SingleAsync()).PapssDecisionPublishedAt);
        gateway.Verify(g => g.SubmitPaymentDecisionAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retry_with_different_payment_content_under_the_same_TxId_still_conflicts()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var inboundFirst = await RecordAsync(storage, harness, "ACCP", TransactionStatus.Pending);
        var publisher = Publisher(storage, harness, Mock.Of<IPapssFacingSipsClient>(MockBehavior.Strict));

        var conflicting = XDocument.Parse(inboundFirst);
        conflicting.Descendants().Single(e => e.Name.LocalName == "InstdAmt").Value = "999.00";

        var error = await Assert.ThrowsAsync<SIPS.Connect.Services.ParticipantRailException>(
            () => publisher.PersistAndSubmitAsync(harness.Binding(), conflicting.ToString(SaveOptions.DisableFormatting), CancellationToken.None));
        Assert.Equal("DUPLICATE_CONFLICT", error.Code);
    }

    [Fact]
    public async Task Timeout_requires_reconciliation_before_any_gateway_decision()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var inbound = await RecordAsync(storage, harness, "RJCT", TransactionStatus.CheckStatus);
        var gateway = new Mock<IPapssFacingSipsClient>(MockBehavior.Strict);

        await Assert.ThrowsAsync<PapssBankDecisionUnresolvedException>(() => Publisher(storage, harness, gateway.Object)
            .PersistAndSubmitAsync(harness.Binding(), inbound, CancellationToken.None));
        Assert.Null((await storage.ISOMessages.AsNoTracking().SingleAsync()).PapssDecision);
        gateway.VerifyNoOtherCalls();
    }

    private static PapssPaymentDecisionPublisher Publisher(IStorageBroker storage, PostgresHarness harness, IPapssFacingSipsClient gateway)
    {
        var signer = new Mock<INativeSigner>();
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), XadesProfile.WpSipsPapss, It.IsAny<string>()))
            .Returns((string xml, XadesProfile profile, string algorithm) => xml);
        return new(storage, gateway, signer.Object, harness.Options, NullLogger<PapssPaymentDecisionPublisher>.Instance);
    }

    private static async Task<string> RecordAsync(IStorageBroker storage, PostgresHarness harness, string status, TransactionStatus storedStatus, bool queued = false)
    {
        var document = XDocument.Parse(PostgresHarness.InboundPayment("PAPSS-BANK-DECISION", "TX-BANK-DECISION", "E2E-BANK-DECISION"));
        var header = document.Descendants().Single(e => e.Name.LocalName == "AppHdr");
        header.Add(new XElement(header.Name.Namespace + "BizSvc", harness.Options.SecurityProfile));
        var inbound = document.ToString(SaveOptions.DisableFormatting);
        var request = PaymentRequestBuilder.Parse(inbound);
        var response = PaymentRequestResponseBuilder.Build(new()
        {
            From = request.To, To = request.From, Original = request, Status = status,
            Reason = status == "RJCT" ? "AM04" : null
        });
        storage.ISOMessages.Add(new ISOMessage
        {
            MessageType = ISOMessageType.TransactionRequest, Status = storedStatus, MsgId = request.MsgId,
            BizMsgIdr = request.BizMsgIdr, MsgDefIdr = request.MsgDefIdr, BusinessService = harness.Options.SecurityProfile,
            TxId = request.TxId, EndToEndId = request.EndToEndId, Date = DateTimeOffset.UtcNow,
            FromBIC = request.From, ToBIC = request.To, Message = Encoding.UTF8.GetBytes(inbound),
            Response = Encoding.UTF8.GetBytes(response), PapssDecision = queued ? Encoding.UTF8.GetBytes("<legacy-decision/>") : null
        });
        await storage.SaveChangesAsync(CancellationToken.None);
        return inbound;
    }
}
