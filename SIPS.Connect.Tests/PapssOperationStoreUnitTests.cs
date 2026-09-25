using System.Text;
using System.Text.Json.Nodes;
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
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;

namespace SIPS.Connect.Tests;

/// <summary>Database-free tests for the PAPSS operation store (the PostgreSQL behaviour is covered by SIPS.Connect.PostgresTests).</summary>
public sealed class PapssOperationStoreUnitTests
{
    [Fact]
    public void Store_settings_are_unset_by_default_and_bind_from_environment_style_keys()
    {
        var defaults = Bind(new Dictionary<string, string?>());
        Assert.Null(defaults.Inbound.Acmt023.ResponseDeadlineSeconds);
        Assert.Null(defaults.Inbound.Acmt023.DeadlineClock);
        Assert.Equal(PapssLateResponsePolicy.Submit, defaults.Inbound.LateResponsePolicy);
        Assert.Null(defaults.Outbound.VerificationResultExpirySeconds);
        Assert.Null(defaults.Store.RetentionDays);
        Assert.Equal(0, defaults.Lookup.MaxWaitSeconds);
        DI.ValidatePapssStore(defaults);

        // docker-compose passes unset optional values as empty strings: they must stay "unset".
        var empty = Bind(new Dictionary<string, string?>
        {
            ["PapssFacing:Inbound:Acmt023:ResponseDeadlineSeconds"] = "",
            ["PapssFacing:Inbound:Acmt023:DeadlineClock"] = "",
            ["PapssFacing:Outbound:VerificationResultExpirySeconds"] = "",
            ["PapssFacing:Store:RetentionDays"] = ""
        });
        Assert.Null(empty.Inbound.Acmt023.ResponseDeadlineSeconds);
        Assert.Null(empty.Inbound.Acmt023.DeadlineClock);
        Assert.Null(empty.Outbound.VerificationResultExpirySeconds);
        Assert.Null(empty.Store.RetentionDays);
        DI.ValidatePapssStore(empty);

        // Environment variables PapssFacing__Inbound__Acmt023__DeadlineClock etc. arrive with ':' separators.
        var configured = Bind(new Dictionary<string, string?>
        {
            ["PapssFacing:Inbound:Acmt023:ResponseDeadlineSeconds"] = "25",
            ["PapssFacing:Inbound:Acmt023:DeadlineClock"] = "ReceivedAt",
            ["PapssFacing:Inbound:LateResponsePolicy"] = "Hold",
            ["PapssFacing:Outbound:VerificationResultExpirySeconds"] = "600",
            ["PapssFacing:Store:RetentionDays"] = "90",
            ["PapssFacing:Delivery:MaxAttempts"] = "3",
            ["PapssFacing:Lookup:MaxWaitSeconds"] = "20"
        });
        DI.ValidatePapssStore(configured);
        Assert.Equal(25, configured.Inbound.Acmt023.ResponseDeadlineSeconds);
        Assert.Equal(PapssDeadlineClock.ReceivedAt, configured.Inbound.Acmt023.DeadlineClock);
        Assert.Equal(PapssLateResponsePolicy.Hold, configured.Inbound.LateResponsePolicy);
        Assert.Equal(600, configured.Outbound.VerificationResultExpirySeconds);
        Assert.Equal(90, configured.Store.RetentionDays);
        Assert.Equal(3, configured.Delivery.MaxAttempts);
        Assert.Equal(20, configured.Lookup.MaxWaitSeconds);
    }

    [Fact]
    public void Deadline_value_and_clock_must_be_configured_together()
    {
        var options = new PapssFacingOptions();
        options.Inbound.Acmt023.ResponseDeadlineSeconds = 30;
        Assert.Throws<InvalidOperationException>(() => DI.ValidatePapssStore(options));
        options.Inbound.Acmt023.DeadlineClock = PapssDeadlineClock.SourceCreationTime;
        DI.ValidatePapssStore(options);
    }

    [Fact]
    public void Deadline_is_computed_only_when_configured()
    {
        var created = new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
        var received = created.AddSeconds(3);
        var options = new PapssFacingOptions();
        var store = new PapssOperationStore(null!, options, TimeProvider.System, NullLogger<PapssOperationStore>.Instance);
        Assert.Null(store.InboundDeadline(created, received));

        options.Inbound.Acmt023.ResponseDeadlineSeconds = 20;
        options.Inbound.Acmt023.DeadlineClock = PapssDeadlineClock.SourceCreationTime;
        Assert.Equal(created.AddSeconds(20), store.InboundDeadline(created, received));
        options.Inbound.Acmt023.DeadlineClock = PapssDeadlineClock.ReceivedAt;
        Assert.Equal(received.AddSeconds(20), store.InboundDeadline(created, received));
    }

    [Fact]
    public void Backoff_is_exponential_and_capped()
    {
        var delivery = new PapssDeliveryOptions { InitialBackoffSeconds = 5, MaxBackoffSeconds = 60 };
        Assert.Equal(TimeSpan.FromSeconds(5), delivery.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(10), delivery.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(40), delivery.Backoff(4));
        Assert.Equal(TimeSpan.FromSeconds(60), delivery.Backoff(9));
    }

    [Theory]
    [InlineData("OUTBOUND", "SUBMITTING", "PENDING", "NOT_REQUIRED", null, "PENDING")]
    [InlineData("OUTBOUND", "ADMITTED", "PENDING", "NOT_REQUIRED", null, "PENDING")]
    [InlineData("OUTBOUND", "SUBMISSION_UNKNOWN", "PENDING", "NOT_REQUIRED", null, "UNKNOWN")]
    [InlineData("OUTBOUND", "REJECTED", "REJECTED", "NOT_REQUIRED", null, "REJECTED")]
    [InlineData("OUTBOUND", "ADMITTED", "VERIFIED_NO_MATCH", "FAILED", null, "COMPLETED")]
    [InlineData("INBOUND", "NOT_SUBMITTED", "PENDING", "PENDING", null, "PENDING")]
    [InlineData("INBOUND", "NOT_SUBMITTED", "UNKNOWN", "FAILED", null, "FAILED")]
    [InlineData("INBOUND", "SUBMISSION_UNKNOWN", "VERIFIED_MATCH", "DELIVERED", "PENDING", "PENDING")]
    [InlineData("INBOUND", "SUBMISSION_UNKNOWN", "VERIFIED_MATCH", "DELIVERED", "FAILED", "UNKNOWN")]
    [InlineData("INBOUND", "ADMITTED", "VERIFIED_MATCH", "DELIVERED", "ADMITTED", "COMPLETED")]
    [InlineData("INBOUND", "REJECTED", "VERIFIED_MATCH", "DELIVERED", "REJECTED", "REJECTED")]
    [InlineData("INBOUND", "NOT_SUBMITTED", "VERIFIED_MATCH", "DELIVERED", "HELD", "FAILED")]
    public void Summary_status_is_derived_from_the_three_states(string direction, string gateway, string outcome, string bank, string? reply, string expected)
    {
        var operation = new PapssOperation
        {
            Direction = Parse<PapssDirection>(direction),
            GatewayState = Parse<PapssGatewayState>(gateway),
            PapssOutcome = Parse<PapssOutcome>(outcome),
            BankDeliveryState = Parse<PapssDeliveryState>(bank),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var response = reply is null ? null : new PapssOutboundResponse { State = Parse<PapssResponseState>(reply) };
        Assert.Equal(expected, PapssOperationResult.Summarize(operation, response, DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Pending_outbound_verification_expires_only_when_configured()
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new PapssOperation { Direction = PapssDirection.Outbound, GatewayState = PapssGatewayState.Admitted, PapssOutcome = PapssOutcome.Pending, CreatedAt = now.AddHours(-5) };
        Assert.Equal("PENDING", PapssOperationResult.Summarize(operation, null, now, null));
        Assert.Equal("EXPIRED", PapssOperationResult.Summarize(operation, null, now, 3600));
        Assert.Equal(18000, PapssOperationResult.From(operation, null, now, null).AgeSeconds);
    }

    [Theory]
    [InlineData(true, "AC01", "AC01")]
    [InlineData(false, "MS03", "MS03")]
    [InlineData(false, "", null)]
    [InlineData(true, "", null)]
    [InlineData(false, "A reason that is far longer than thirty five characters", null)]
    public void Reply_carries_exactly_the_core_bank_reason_or_none(bool verified, string bankReason, string? expected)
    {
        var request = PayeeVerificationBuilder.Parse(PayeeVerificationBuilder.Build(new() { From = "PHBXSLFR", To = "ZKBASOS0", Alias = "0012", Type = "BBAN", MsgId = "P-MSG-1", SIPSRequestId = "P-VID-1" }).document);
        var answer = CoreBankVerificationClient.InitialResponse(request);
        answer.Verified = verified;
        answer.Name = verified ? "AMINA" : string.Empty;
        answer.Reason = bankReason;
        var service = new PapssInboundVerificationService(null!, null!, null!, new PapssFacingOptions(), null!, null!, new CorrelationService(), NullLogger<PapssInboundVerificationService>.Instance);

        var xml = service.ApplyBankReason(PayeeVerificationResponseBuilder.Build(answer), bankReason, "P-MSG-1");

        var reasons = XDocument.Parse(xml).Descendants().Where(x => x.Name.LocalName == "Rsn").ToList();
        if (expected is null) Assert.Empty(reasons);
        else Assert.Equal(expected, Assert.Single(reasons).Elements().Single().Value);
    }

    [Fact]
    public async Task Stored_papss_result_is_acknowledged_without_inline_bank_delivery()
    {
        var context = new ParticipantCallbackContext();
        var inbox = new Mock<IVerificationResultInbox>();
        inbox.Setup(x => x.AcceptAsync(It.IsAny<string>(), It.IsAny<PayeeVerificationResponseBuilder.Request>(), It.IsAny<CBVerificationResultDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VerificationResultInboxOutcome.Stored);
        var callback = new Mock<ICallbackClient>(MockBehavior.Strict);
        var handler = Handler(context, callback.Object, inbox.Object);

        string result;
        using (context.Push(new PapssParticipantBinding("ZKBASOS0", "SO", ["USD"], "papss-callback-v1", "https://bank.test/cb")))
            result = await handler.HandleAsync(Report(), CancellationToken.None);

        Assert.Equal(string.Empty, result);
        inbox.Verify(x => x.AcceptAsync(It.IsAny<string>(), It.IsAny<PayeeVerificationResponseBuilder.Request>(), It.Is<CBVerificationResultDto>(d => d.RequestMessageId == "SIPS-REQ-1"), It.IsAny<CancellationToken>()), Times.Once);
        callback.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Papss_acmt023_is_answered_by_the_inbound_service_and_acknowledged()
    {
        var inbound = new Mock<IPapssInboundVerificationService>();
        var controller = Controller(inbound.Object, out var incoming);
        var xml = PayeeVerificationBuilder.Build(new() { From = "PHBXSLFR", To = "ZKBASOS0", Alias = "0012", Type = "BBAN", MsgId = "P-MSG-1", SIPSRequestId = "P-VID-1" }).document;
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var result = await controller.Post(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        inbound.Verify(x => x.HandleAsync(It.IsAny<PapssParticipantBinding>(), xml, It.IsAny<CancellationToken>()), Times.Once);
        incoming.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Papss_callback_that_could_not_be_stored_is_not_acknowledged()
    {
        var inbound = new Mock<IPapssInboundVerificationService>();
        inbound.Setup(x => x.HandleAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var controller = Controller(inbound.Object, out _);
        var xml = PayeeVerificationBuilder.Build(new() { From = "PHBXSLFR", To = "ZKBASOS0", Alias = "0012", Type = "BBAN", MsgId = "P-MSG-2", SIPSRequestId = "P-VID-2" }).document;
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var result = await controller.Post(CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private static IncomingController Controller(IPapssInboundVerificationService inbound, out Mock<IIncoming> incoming)
    {
        incoming = new Mock<IIncoming>(MockBehavior.Strict);
        var guard = new Mock<IPapssCallbackGuard>();
        guard.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PapssParticipantBinding("ZKBASOS0", "SO", ["USD"], "papss-callback-v1", "https://bank.test/cb"));
        return new IncomingController(incoming.Object, guard.Object, Mock.Of<IPapssPaymentDecisionPublisher>(), inbound, new ParticipantCallbackContext(), NullLogger<IncomingController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static IncomingVerificationResponseHandler Handler(ParticipantCallbackContext context, ICallbackClient callback, IVerificationResultInbox inbox)
    {
        var signer = new Mock<INativeSigner>();
        signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((xml, _) => xml);
        return new IncomingVerificationResponseHandler(
            new ISO20022Options { Verification = "https://legacy.test/verify" }, NullLogger<IncomingVerificationResponseHandler>.Instance,
            signer.Object, Mock.Of<SIPS.Adapter.IJsonAdapter>(), Mock.Of<ISignatureService>(), new CorrelationService(), callback,
            Mock.Of<ICallbackOrchestrator>(MockBehavior.Strict), context, inbox);
    }

    private static string Report() => PayeeVerificationResponseBuilder.Build(new PayeeVerificationResponseBuilder.Request
    {
        From = "PHBXSLFR", To = "ZKBASOS0", CreDt = DateTime.UtcNow,
        Original = new PayeeVerificationBuilder.Request { From = "ZKBASOS0", To = "PHBXSLFR", BizMsgIdr = "SIPS-REQ-1", MsgDefIdr = "acmt.023.001.03", MsgId = "SIPS-REQ-1", CreDt = DateTime.UtcNow, Alias = "0012", Type = "BBAN", SIPSRequestId = "SIPS-REQ-1" },
        Verified = true, VerificationId = "SIPS-REQ-1", Id = "0012", Type = "BBAN", Name = "AMINA", Currency = "USD", Reason = "MATCH"
    });

    private static PapssFacingOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new PapssFacingOptions();
        configuration.GetSection(PapssFacingOptions.SectionName).Bind(options);
        return options;
    }

    private static T Parse<T>(string value) where T : struct, Enum => Enum.Parse<T>(value.Replace("_", string.Empty), true);
}

public sealed class PapssOperationStoreRegistrationTests
{
    [Fact]
    public void Application_container_resolves_the_operation_store_services()
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
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpClient();
        services.AddDistributedMemoryCache();
        DI.Register(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<PapssVerificationResultInbox>(scope.ServiceProvider.GetRequiredService<IVerificationResultInbox>());
        Assert.IsType<IncomingVerificationResponseHandler>(scope.ServiceProvider.GetRequiredService<IVerificationResultDelivery>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPapssInboundVerificationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PapssOperationStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICoreBankVerificationClient>());
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Select(x => x.GetType()).ToArray();
        Assert.Contains(typeof(PapssBankPushWorker), hosted);
        Assert.Contains(typeof(PapssResponseOutboxWorker), hosted);
        Assert.Contains(typeof(PapssStoreRetentionWorker), hosted);
    }
}
