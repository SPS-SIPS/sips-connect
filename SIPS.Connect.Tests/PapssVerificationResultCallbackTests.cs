using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class PapssVerificationResultCallbackTests
{
    private const string LocalBic = "ZKBASOS0";
    private const string PapssSideBic = "PHBXSLFR";
    private const string OriginalMsgId = "SIPS-4f1c2d3e4f5a6b7c8d9e0f1a";
    private const string CallbackUrl = "https://bank.test/papss/callback";
    private const string AccountName = "FORTRESS GLOBAL SECURITY PRINTERS(SL)LTD";

    [Fact]
    public async Task Acmt024_is_routed_to_the_verification_result_handler()
    {
        var resultHandler = new Mock<IIncomingVerificationResponseHandler>();
        resultHandler.Setup(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("DELIVERED");
        var verification = new Mock<IIncomingVerificationHandler>(MockBehavior.Strict);
        var incoming = new Incoming(
            verification.Object,
            Mock.Of<IIncomingTransactionHandler>(),
            Mock.Of<IIncomingTransactionStatusHandler>(),
            Mock.Of<IIncomingReturnTransactionHandler>(),
            Mock.Of<IIncomingPaymentStatusReportHandler>(),
            resultHandler.Object);
        var report = VerifiedReport();

        var result = await incoming.Handle(report, CancellationToken.None);

        Assert.Equal("DELIVERED", result);
        resultHandler.Verify(x => x.HandleAsync(report, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verified_papss_result_is_delivered_to_the_bank_callback_with_correlation_fields()
    {
        var scenario = new Scenario();

        string result;
        using (scenario.Context.Push(Binding()))
            result = await scenario.Handler.HandleAsync(VerifiedReport(), CancellationToken.None);

        Assert.Equal(string.Empty, result);
        Assert.Equal(CallbackUrl, scenario.DeliveredUrl);
        Assert.Equal("SIPS-VERIFY-ID-1", scenario.DeliveredHeaders!["X-Idempotency-Key"]);
        var json = scenario.DeliveredJson!;
        Assert.Equal(OriginalMsgId, json["requestMessageId"]!.GetValue<string>());
        Assert.Equal(OriginalMsgId, json["originalMsgId"]!.GetValue<string>());
        Assert.Equal("SIPS-VERIFY-ID-1", json["verificationId"]!.GetValue<string>());
        Assert.True(json["verified"]!.GetValue<bool>());
        Assert.Equal("0012030321735", json["accountNumber"]!.GetValue<string>());
        Assert.Equal("BBAN", json["accountType"]!.GetValue<string>());
        Assert.Equal(AccountName, json["accountName"]!.GetValue<string>());
        Assert.Equal("SLE", json["currency"]!.GetValue<string>());
        Assert.Equal("MATCH", json["reason"]!.GetValue<string>());
        Assert.True(json.ContainsKey("additionalInfo"));
        Assert.Equal(PapssSideBic, json["fromBIC"]!.GetValue<string>());
        Assert.Equal(LocalBic, json["toBIC"]!.GetValue<string>());
        // Authenticated by the PAPSS callback guard: the legacy IPS signature profile is not re-applied.
        scenario.Signature.Verify(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unverified_result_still_carries_the_original_account_and_reason()
    {
        var scenario = new Scenario();

        using (scenario.Context.Push(Binding()))
            await scenario.Handler.HandleAsync(Report(verified: false), CancellationToken.None);

        var json = scenario.DeliveredJson!;
        Assert.False(json["verified"]!.GetValue<bool>());
        Assert.Equal("MISS", json["reason"]!.GetValue<string>());
        Assert.Equal("0012030321735", json["accountNumber"]!.GetValue<string>());
        Assert.Equal("BBAN", json["accountType"]!.GetValue<string>());
        Assert.Equal(OriginalMsgId, json["requestMessageId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rejected_bank_delivery_is_reported_as_a_retryable_failure()
    {
        var scenario = new Scenario { CallbackStatus = HttpStatusCode.ServiceUnavailable };

        using (scenario.Context.Push(Binding()))
        {
            var error = await Assert.ThrowsAsync<CallbackDeliveryException>(() => scenario.Handler.HandleAsync(VerifiedReport(), CancellationToken.None));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        }
    }

    [Fact]
    public async Task Unauthenticated_report_with_invalid_signature_is_rejected_without_callback()
    {
        var scenario = new Scenario();
        scenario.Signature.Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((false, "invalid"));

        var result = await scenario.Handler.HandleAsync(VerifiedReport(), CancellationToken.None);

        var header = XDocument.Parse(result).Descendants().First(x => x.Name.LocalName == "AppHdr");
        Assert.StartsWith("admi.002", header.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").Value);
        Assert.Null(scenario.DeliveredJson);
    }

    private static PapssParticipantBinding Binding() => new(LocalBic, "SO", ["USD"], "papss-callback-v1", CallbackUrl);

    private static string VerifiedReport() => Report(verified: true);

    // Mirrors the PAPSS gateway's InstitutionCallbackMapper.MapVerification output.
    private static string Report(bool verified) => PayeeVerificationResponseBuilder.Build(new PayeeVerificationResponseBuilder.Request
    {
        From = PapssSideBic,
        To = LocalBic,
        CreDt = DateTime.UtcNow,
        Original = new PayeeVerificationBuilder.Request
        {
            From = LocalBic,
            To = PapssSideBic,
            BizMsgIdr = OriginalMsgId,
            MsgDefIdr = "acmt.023.001.03",
            MsgId = OriginalMsgId,
            CreDt = DateTime.UtcNow.AddSeconds(-5),
            Alias = "0012030321735",
            Type = "BBAN",
            SIPSRequestId = "SIPS-VERIFY-ID-1"
        },
        Verified = verified,
        VerificationId = "SIPS-VERIFY-ID-1",
        Id = "0012030321735",
        Type = "BBAN",
        Name = verified ? AccountName : string.Empty,
        Currency = verified ? "SLE" : string.Empty,
        Reason = verified ? "MATCH" : "MISS"
    });

    private sealed class Scenario
    {
        public ParticipantCallbackContext Context { get; } = new();
        public Mock<ISignatureService> Signature { get; } = new();
        public HttpStatusCode CallbackStatus { get; init; } = HttpStatusCode.OK;
        public string? DeliveredUrl { get; private set; }
        public Dictionary<string, string>? DeliveredHeaders { get; private set; }
        public JsonObject? DeliveredJson { get; private set; }
        public IncomingVerificationResponseHandler Handler { get; }

        public Scenario()
        {
            var options = LoadRepositoryMappings();
            var adapter = new ParticipantCallbackJsonAdapter(new JsonAdapter(options, NullLogger<JsonAdapter>.Instance), options, Context);
            var http = new Mock<IInterfaceHttpClient>();
            http.Setup(x => x.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
                .Returns(async (string url, Dictionary<string, string>? headers, StringContent content, CancellationToken ct) =>
                {
                    DeliveredUrl = url;
                    DeliveredHeaders = headers;
                    DeliveredJson = JsonNode.Parse(await content.ReadAsStringAsync(ct))!.AsObject();
                    return new Response<JsonObject?>(new JsonObject()) { StatusCode = CallbackStatus, IsSuccess = CallbackStatus == HttpStatusCode.OK };
                });
            var callbackClient = new ParticipantCallbackClient(new CallbackClient(http.Object, NullLogger<CallbackClient>.Instance, new CorrelationService()), Context);
            var signer = new Mock<INativeSigner>();
            signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((xml, _) => xml);
            Handler = new IncomingVerificationResponseHandler(
                new ISO20022Options { Verification = "https://legacy.test/verify", Key = "key", Secret = "secret" },
                NullLogger<IncomingVerificationResponseHandler>.Instance,
                signer.Object,
                adapter,
                Signature.Object,
                new CorrelationService(),
                callbackClient,
                new CallbackOrchestrator(),
                Context);
        }
    }

    private static JsonAdapterOptions LoadRepositoryMappings()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "jsonAdapter.json")))
            current = current.Parent;
        var path = Path.Combine(current?.FullName ?? throw new DirectoryNotFoundException("Could not locate the SIPS repository root."), "jsonAdapter.json");
        return JsonSerializer.Deserialize<JsonAdapterOptions>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
