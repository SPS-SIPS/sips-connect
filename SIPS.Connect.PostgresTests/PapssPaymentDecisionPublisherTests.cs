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

    [Fact]
    public async Task Timeout_requires_reconciliation_before_any_gateway_decision()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var inbound = await RecordAsync(storage, harness, "RJCT", TransactionStatus.CheckStatus);
        var gateway = new Mock<IPapssFacingSipsClient>(MockBehavior.Strict);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(storage, harness, gateway.Object)
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
