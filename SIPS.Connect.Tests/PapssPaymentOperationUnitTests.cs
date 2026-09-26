using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Xunit;
using Rules = SIPS.Connect.Services.PapssPaymentStatusRules;

namespace SIPS.Connect.Tests;

/// <summary>Database-free tests for PAPSS payments/returns (the PostgreSQL behaviour is covered by SIPS.Connect.PostgresTests).</summary>
public sealed class PapssPaymentOperationUnitTests
{
    private static readonly string[] AcscOnly = ["ACSC"];

    [Theory]
    // Non-final payment: statuses only move forward.
    [InlineData("PENDING", null, "ACCP", "APPLIED")]
    [InlineData("PENDING", null, "PDNG", "APPLIED")]
    [InlineData("PENDING", null, "ACSP", "APPLIED")]
    [InlineData("ACCEPTED", "ACCP", "ACSP", "APPLIED")]
    [InlineData("ACCEPTED", "ACSP", "ACCP", "NOT_ADVANCING")]
    [InlineData("ACCEPTED", "ACSP", "ACSP", "NOT_ADVANCING")]
    [InlineData("ACCEPTED", "ACSP", "PDNG", "NOT_ADVANCING")]
    [InlineData("ACCEPTED", "ACSP", "ACSC", "APPLIED")]
    [InlineData("ACCEPTED", "ACCP", "RJCT", "APPLIED")]
    [InlineData("UNKNOWN", null, "ACSC", "APPLIED")]
    // Final payment: never regressed; the same final again is a duplicate; a different final is a conflict.
    [InlineData("SETTLED", "ACSC", "ACSP", "NOT_ADVANCING")]
    [InlineData("SETTLED", "ACSC", "ACSC", "DUPLICATE_FINAL")]
    [InlineData("SETTLED", "ACSC", "RJCT", "CONFLICT")]
    [InlineData("REJECTED", "RJCT", "ACSC", "CONFLICT")]
    [InlineData("REJECTED", "RJCT", "RJCT", "DUPLICATE_FINAL")]
    [InlineData("RETURNED", "ACSC", "ACSC", "DUPLICATE_FINAL")]
    [InlineData("RETURNED", "ACSC", "RJCT", "CONFLICT")]
    // Codes outside the gateway's ToSipsStatus set are kept but never applied.
    [InlineData("PENDING", null, "XXXX", "UNKNOWN_STATUS")]
    [InlineData("PENDING", null, null, "UNKNOWN_STATUS")]
    public void Payment_statuses_only_advance_and_final_states_are_never_regressed(string outcome, string? raw, string? status, string expected)
        => Assert.Equal(expected, Rules.Evaluate(Parse<PapssOutcome>(outcome), raw, status, isReturn: false, AcscOnly));

    [Fact]
    public void Return_settlement_status_is_configurable_because_papss_contradicts_itself()
    {
        Assert.Equal(PapssOutcome.Accepted, Rules.OutcomeOf("ACSP", isReturn: true, AcscOnly));
        Assert.Equal(PapssOutcome.Settled, Rules.OutcomeOf("ACSC", isReturn: true, AcscOnly));
        Assert.Equal(PapssOutcome.Settled, Rules.OutcomeOf("ACSP", isReturn: true, ["ACSC", "ACSP"]));
        // Default (PAPSS-confirmed 2026-09-26): ACCP is the returner's authoritative return status.
        var defaults = new SIPS.Connect.Config.PapssReturnOptions().SettledStatusList();
        Assert.Equal(PapssOutcome.Settled, Rules.OutcomeOf("ACCP", isReturn: true, defaults));
        Assert.Equal(PapssEventDisposition.Applied, Rules.Evaluate(PapssOutcome.Pending, null, "ACCP", isReturn: true, defaults));
        Assert.Equal(PapssEventDisposition.DuplicateFinal, Rules.Evaluate(PapssOutcome.Settled, "ACCP", "ACSC", isReturn: true, defaults));
        Assert.Equal(PapssOutcome.Accepted, Rules.OutcomeOf("ACCP", isReturn: false, defaults));
        // The setting only affects returns.
        Assert.Equal(PapssOutcome.Accepted, Rules.OutcomeOf("ACSP", isReturn: false, ["ACSC", "ACSP"]));
    }

    [Fact]
    public void Status_report_is_read_by_exact_path_from_the_sips_pacs002_shape()
    {
        var xml = Pacs002("PAPSS-SRC-1", "ZKBASOS0-ORIG-MSG", "pacs.008.001.10", "TX-1", "E2E-1", "RJCT", reason: "AM04", amount: 12.5m);
        var report = PapssPaymentMessages.ParseStatusReport(xml);
        Assert.Equal("PAPSS-SRC-1", report.SourceMessageId);
        Assert.Equal("ZKBASOS0-ORIG-MSG", report.OriginalMessageId);
        Assert.Equal("pacs.008.001.10", report.OriginalMessageType);
        Assert.Equal("TX-1", report.OriginalTxId);
        Assert.Equal("E2E-1", report.OriginalEndToEndId);
        Assert.Equal("RJCT", report.Status);
        Assert.Equal("AM04", report.ReasonCode);
        Assert.Equal(12.5m, report.Amount);
        Assert.Equal("USD", report.Currency);
    }

    [Fact]
    public void Status_reason_is_read_from_cd_as_well_as_prtry_and_other_cd_elements_are_ignored()
    {
        var document = XDocument.Parse(Pacs002("PAPSS-SRC-2", "M", "pacs.008.001.10", "TX-2", "E2E-2", "RJCT", reason: "AM04"));
        var rsn = document.Descendants().Single(x => x.Name.LocalName == "Rsn");
        var prtry = rsn.Elements().Single();
        prtry.ReplaceWith(new XElement(prtry.Name.Namespace + "Cd", "AC01"));
        // A Cd elsewhere in the message (as in PAPSS LclInstrm/CtgyPurp) must not be picked up.
        var tx = document.Descendants().Single(x => x.Name.LocalName == "TxInfAndSts");
        tx.Add(new XElement(tx.Name.Namespace + "Unrelated", new XElement(tx.Name.Namespace + "Cd", "USDP")));
        Assert.Equal("AC01", PapssPaymentMessages.ParseStatusReport(document.ToString()).ReasonCode);
    }

    [Fact]
    public void Status_report_without_an_original_transaction_is_rejected()
    {
        var document = XDocument.Parse(Pacs002("PAPSS-SRC-3", "M", "pacs.008.001.10", "TX-3", "E2E-3", "ACSC"));
        document.Descendants().Single(x => x.Name.LocalName == "OrgnlTxId").Remove();
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(document.ToString()));
    }

    [Fact]
    public void Inbound_return_and_payment_are_read_from_the_sips_shapes()
    {
        var pacs004 = ReturnPaymentRequestBuilder.Build(new() { From = "PHBXSLFR", To = "ZKBASOS0", CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = "USDP", CategoryPurpose = "CASH", ReturnId = "RTN-1", OrgnlTxId = "TX-9", OriginalEndToEnd = "E2E-9", OriginalCurrency = "USD", OriginalAmount = 10m, ReturnReason = "FOCR", AdditionalInfo = "requested", DebtorAgent = "PHBXSLFR", CreditorAgent = "ZKBASOS0" }).document;
        var r = PapssPaymentMessages.ParseReturn(pacs004);
        Assert.Equal(("RTN-1", "TX-9", "E2E-9", 10m, "USD", "FOCR", "USDP", "PHBXSLFR"), (r.ReturnId, r.OriginalTxId, r.OriginalEndToEndId, r.Amount!.Value, r.Currency, r.ReasonCode, r.LocalInstrument, r.InstructingAgent));

        var pacs008 = Pacs008("TX-IN", "E2E-IN", 25m);
        var p = PapssPaymentMessages.ParsePayment(pacs008);
        Assert.Equal(("TX-IN", "E2E-IN", 25m, "USD", "USDP", "PHBXSLFR"), (p.TxId, p.EndToEndId, p.Amount!.Value, p.Currency, p.LocalInstrument, p.DebtorAgent));
    }

    [Fact]
    public void Request_fingerprint_ignores_formatting_but_not_content()
    {
        var a = Payment();
        var b = Payment(); b.Currency = " usd "; b.ToBIC = "phbxslfr"; b.Amount = 10.00m;
        Assert.Equal(PapssPaymentService.Fingerprint(a), PapssPaymentService.Fingerprint(b));
        var c = Payment(); c.Amount = 10.01m;
        Assert.NotEqual(PapssPaymentService.Fingerprint(a), PapssPaymentService.Fingerprint(c));
        var d = Payment(); d.CreditorAccount = "999";
        Assert.NotEqual(PapssPaymentService.Fingerprint(a), PapssPaymentService.Fingerprint(d));
    }

    [Theory]
    [InlineData("PAYMENT", "OUTBOUND", "ADMITTED", "ACCEPTED", "PENDING")]
    [InlineData("PAYMENT", "OUTBOUND", "ADMITTED", "SETTLED", "COMPLETED")]
    [InlineData("PAYMENT", "OUTBOUND", "ADMITTED", "RETURNED", "COMPLETED")]
    [InlineData("PAYMENT", "OUTBOUND", "REJECTED", "REJECTED", "REJECTED")]
    [InlineData("PAYMENT", "OUTBOUND", "SUBMISSION_UNKNOWN", "PENDING", "UNKNOWN")]
    [InlineData("PAYMENT", "OUTBOUND", "SUBMISSION_UNKNOWN", "SETTLED", "COMPLETED")]
    [InlineData("PAYMENT", "INBOUND", "ADMITTED", "ACCEPTED", "PENDING")]
    [InlineData("PAYMENT", "INBOUND", "ADMITTED", "REJECTED", "REJECTED")]
    [InlineData("RETURN", "INBOUND", "NOT_SUBMITTED", "SETTLED", "COMPLETED")]
    public void Payment_summary_status_is_derived_from_the_payment_outcome(string operation, string direction, string gateway, string outcome, string expected)
    {
        var op = new PapssOperation
        {
            Operation = Parse<PapssOperationType>(operation),
            Direction = Parse<PapssDirection>(direction),
            GatewayState = Parse<PapssGatewayState>(gateway),
            PapssOutcome = Parse<PapssOutcome>(outcome),
            BankDeliveryState = PapssDeliveryState.NotRequired,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Assert.Equal(expected, PapssOperationResult.Summarize(op, null, DateTimeOffset.UtcNow, 1));
    }

    [Fact]
    public void Status_and_return_settings_are_unset_or_safe_by_default_and_bind_from_environment_style_keys()
    {
        var defaults = Bind(new Dictionary<string, string?>());
        Assert.Null(defaults.Status.EnquiryMinimumAgeSeconds);
        // PAPSS-confirmed 2026-09-26: the returner's authoritative return status is ACCP.
        Assert.Equal(["ACCP", "ACSC"], defaults.Returns.SettledStatusList());
        Assert.True(defaults.Recall.RequireSettledOriginal);
        Assert.Equal((30, 30), (defaults.Recall.MaxAgeDays, defaults.Recall.ResponseDeadlineDays));
        DI.ValidatePapssStore(defaults);

        var empty = Bind(new Dictionary<string, string?> { ["PapssFacing:Status:EnquiryMinimumAgeSeconds"] = "" });
        Assert.Null(empty.Status.EnquiryMinimumAgeSeconds);
        DI.ValidatePapssStore(empty);

        var configured = Bind(new Dictionary<string, string?>
        {
            ["PapssFacing:Status:EnquiryMinimumAgeSeconds"] = "120",
            ["PapssFacing:Returns:SettledStatuses"] = "acsc, ACSP"
        });
        DI.ValidatePapssStore(configured);
        Assert.Equal(120, configured.Status.EnquiryMinimumAgeSeconds);
        Assert.Equal(["ACSC", "ACSP"], configured.Returns.SettledStatusList());

        DI.ValidatePapssStore(Bind(new() { ["PapssFacing:Returns:SettledStatuses"] = "ACCP" }));
        Assert.Throws<InvalidOperationException>(() => DI.ValidatePapssStore(Bind(new() { ["PapssFacing:Returns:SettledStatuses"] = "PDNG" })));
        var recall = Bind(new() { ["PapssFacing:Recall:RequireSettledOriginal"] = "false", ["PapssFacing:Recall:MaxAgeDays"] = "", ["PapssFacing:Recall:ResponseDeadlineDays"] = "45" });
        // An empty value keeps the PAPSS-confirmed default.
        Assert.Equal((false, (int?)30, (int?)45), (recall.Recall.RequireSettledOriginal, recall.Recall.MaxAgeDays, recall.Recall.ResponseDeadlineDays));
        DI.ValidatePapssStore(recall);
        Assert.Throws<InvalidOperationException>(() => DI.ValidatePapssStore(Bind(new() { ["PapssFacing:Recall:MaxAgeDays"] = "0" })));
        Assert.Throws<InvalidOperationException>(() => DI.ValidatePapssStore(Bind(new() { ["PapssFacing:Recall:ResponseDeadlineDays"] = "-1" })));
        Assert.Throws<InvalidOperationException>(() => DI.ValidatePapssStore(Bind(new() { ["PapssFacing:Returns:SettledStatuses"] = " " })));
        Assert.Throws<InvalidOperationException>(() => DI.ValidatePapssStore(Bind(new() { ["PapssFacing:Status:EnquiryMinimumAgeSeconds"] = "0" })));
    }

    [Theory]
    [InlineData("pacs.002.001.12")]
    [InlineData("pacs.004.001.11")]
    public async Task Papss_pacs002_and_pacs004_are_stored_by_the_payment_service_and_never_reach_the_smartvista_handlers(string definition)
    {
        var callbacks = new Mock<IPapssPaymentCallbackService>();
        var controller = Controller(callbacks.Object, out var incoming, out var publisher);
        var xml = definition == "pacs.002.001.12"
            ? Pacs002("PAPSS-SRC-9", "M", "pacs.008.001.10", "TX-9", "E2E-9", "ACSC")
            : ReturnPaymentRequestBuilder.Build(new() { From = "WPSIPSGW", To = "ZKBASOS0", CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = "USDP", CategoryPurpose = "CASH", ReturnId = "RTN-9", OrgnlTxId = "TX-9", OriginalEndToEnd = "E2E-9", OriginalCurrency = "USD", OriginalAmount = 1m, ReturnReason = "FOCR", DebtorAgent = "PHBXSLFR", CreditorAgent = "ZKBASOS0" }).document;
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var result = await controller.Post(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        if (definition == "pacs.002.001.12")
            callbacks.Verify(x => x.HandleStatusReportAsync(It.IsAny<PapssParticipantBinding>(), xml, It.IsAny<CancellationToken>()), Times.Once);
        else
            callbacks.Verify(x => x.HandleReturnAsync(It.IsAny<PapssParticipantBinding>(), xml, It.IsAny<CancellationToken>()), Times.Once);
        incoming.VerifyNoOtherCalls();
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Papss_pacs002_that_could_not_be_stored_is_not_acknowledged_and_an_unparseable_one_is_refused()
    {
        var callbacks = new Mock<IPapssPaymentCallbackService>();
        callbacks.SetupSequence(x => x.HandleStatusReportAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"))
            .ThrowsAsync(new InvalidDataException("The PAPSS message is missing OrgnlTxId."));
        var xml = Pacs002("PAPSS-SRC-10", "M", "pacs.008.001.10", "TX-10", "E2E-10", "ACSC");

        var controller = Controller(callbacks.Object, out _, out _);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Post(CancellationToken.None)).StatusCode);

        controller = Controller(callbacks.Object, out _, out _);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Assert.IsType<BadRequestObjectResult>(await controller.Post(CancellationToken.None));
    }

    [Fact]
    public async Task Papss_pacs008_keeps_the_legacy_handler_and_decision_outbox_and_is_recorded()
    {
        var callbacks = new Mock<IPapssPaymentCallbackService>(MockBehavior.Strict);
        var order = new List<string>();
        callbacks.Setup(x => x.RecordInboundPaymentAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("record")).Returns(Task.CompletedTask);
        callbacks.Setup(x => x.SyncInboundPaymentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("sync")).Returns(Task.CompletedTask);
        var controller = Controller(callbacks.Object, out var incoming, out var publisher);
        incoming.Setup(x => x.Handle(It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("handler")).ReturnsAsync("<ok/>");
        publisher.Setup(x => x.PersistAndSubmitAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback(() => order.Add("publish")).Returns(Task.CompletedTask);
        var xml = Pacs008("T", "E", 1m);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.IsType<OkResult>(await controller.Post(CancellationToken.None));
        Assert.Equal(["handler", "record", "publish", "sync"], order);
    }

    [Fact]
    public void Application_container_resolves_the_payment_services()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:db"] = "Host=localhost;Database=unused;Username=postgres",
            ["Xades:WithoutPKI"] = "true",
            ["Xades:BIC"] = "ZKBASOS0",
            ["Keycloak:Realm:Audience"] = "sips",
            ["Core:SAFExpression"] = "*/5 * * * *",
            ["Core:SAFTimeZoneInfo"] = "UTC"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpClient();
        services.AddDistributedMemoryCache();
        DI.Register(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<PapssPaymentService>(scope.ServiceProvider.GetRequiredService<IPapssPaymentService>());
        Assert.IsType<PapssPaymentCallbackService>(scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PapssPaymentEventDelivery>());
        // The pacs handlers get the PAPSS pre-authentication flag instead of re-verifying with the legacy profile.
        Assert.IsType<ParticipantCallbackContext>(provider.GetRequiredService<IInboundAuthenticationContext>());
    }

    // ---------------------------------------------------------------------------------------------

    internal static string Pacs002(string sourceMessageId, string originalMessageId, string originalMessageType, string txId, string endToEndId, string status, string? reason = null, decimal amount = 10m)
    {
        var xml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = "WPSIPSGW",
            To = "ZKBASOS0",
            Original = new PaymentRequestBuilder.Request
            {
                From = "ZKBASOS0", To = "PHBXSLFR", BizMsgIdr = originalMessageId, MsgDefIdr = originalMessageType, MsgId = originalMessageId,
                CreDt = DateTime.UtcNow.AddMinutes(-1), EndToEndId = endToEndId, TxId = txId, Amount = amount, Currency = "USD"
            },
            Status = status,
            Reason = reason,
            AdditionalInfo = reason is null ? null : "reported by PAPSS"
        });
        var document = XDocument.Parse(xml);
        document.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == "BizMsgIdr").Value = sourceMessageId;
        return document.ToString(SaveOptions.DisableFormatting);
    }

    internal static string Pacs008(string txId, string endToEndId, decimal amount) => PaymentRequestBuilder.Build(new()
    {
        From = "PHBXSLFR", To = "ZKBASOS0", LocalInstrument = "USDP", CategoryPurpose = "CASH", EndToEndId = endToEndId, TxId = txId, Amount = amount, Currency = "USD", Ustrd = "invoice 7",
        Debtor = new() { Name = "AMINA ALI", Account = "100200", AccountType = "BBAN", AgentBIC = "PHBXSLFR", Issuer = "C" },
        Creditor = new() { Name = "FORTRESS GLOBAL", Account = "0012030321735", AccountType = "BBAN", AgentBIC = "ZKBASOS0", Issuer = "C" }
    }).document;

    private static PaymentRequestDto Payment() => new()
    {
        ToBIC = "PHBXSLFR", TxId = "TX-1", EndToEndId = "E2E-1", Amount = 10m, Currency = "USD", LocalInstrument = "USDP", CategoryPurpose = "CASH",
        DebtorName = "A", DebtorAccount = "1", DebtorAccountType = "BBAN", DebtorAgentBIC = "ZKBASOS0",
        CreditorName = "B", CreditorAccount = "2", CreditorAccountType = "BBAN", CreditorAgentBIC = "PHBXSLFR", RemittanceInformation = "rent"
    };

    private static IncomingController Controller(IPapssPaymentCallbackService callbacks, out Mock<IIncoming> incoming, out Mock<IPapssPaymentDecisionPublisher> publisher)
    {
        incoming = new Mock<IIncoming>(MockBehavior.Strict);
        publisher = new Mock<IPapssPaymentDecisionPublisher>(MockBehavior.Strict);
        var guard = new Mock<IPapssCallbackGuard>();
        guard.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PapssParticipantBinding("ZKBASOS0", "SO", ["USD"], "papss-callback-v1", "https://bank.test/cb"));
        return new IncomingController(incoming.Object, guard.Object, publisher.Object, Mock.Of<IPapssInboundVerificationService>(), new ParticipantCallbackContext(), NullLogger<IncomingController>.Instance, callbacks)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static PapssFacingOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new PapssFacingOptions();
        configuration.GetSection(PapssFacingOptions.SectionName).Bind(options);
        return options;
    }

    private static T Parse<T>(string value) where T : struct, Enum => Enum.Parse<T>(value.Replace("_", string.Empty), true);
}
