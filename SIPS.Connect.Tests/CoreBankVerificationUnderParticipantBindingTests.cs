using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SIPS.Adapter;
using SIPS.Connect.Services;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using Xunit;

namespace SIPS.Connect.Tests;

/// <summary>
/// An inbound PAPSS acmt.023 enquiry runs inside an active PAPSS participant binding (pushed by
/// PapssCallbackGuard before the request is processed) and the corebank lookup it triggers goes through
/// the same ICallbackClient as everything else PAPSS-side: ParticipantCallbackClient. These tests pin
/// that CoreBankVerificationClient.VerifyAsync reaches options.Verification directly instead of being
/// redirected to the binding's bank-notification CallbackUrl, which is what made every inbound PAPSS
/// enquiry answer from whatever that CallbackUrl returned instead of a real corebank lookup.
/// </summary>
public sealed class CoreBankVerificationUnderParticipantBindingTests
{
    private const string LocalBic = "ZKBASOS0";
    private const string PapssSideBic = "PHBXSLFR";
    private const string CoreBankUrl = "https://corebank.test/verify";
    private const string ParticipantCallbackUrl = "https://bank.test/papss/callback";

    [Fact]
    public async Task VerifyAsync_reaches_the_corebank_url_not_the_participant_callback_url()
    {
        var scenario = new Scenario();

        CoreBankVerificationResult result;
        using (scenario.Context.Push(Binding()))
            result = await scenario.Client.VerifyAsync(Request(), "cid", CancellationToken.None);

        Assert.Equal(CoreBankUrl, scenario.CalledUrl);
        Assert.True(result.Answered);
    }

    [Fact]
    public async Task VerifyAsync_reaches_the_corebank_url_even_without_an_active_binding()
    {
        var scenario = new Scenario();

        var result = await scenario.Client.VerifyAsync(Request(), "cid", CancellationToken.None);

        Assert.Equal(CoreBankUrl, scenario.CalledUrl);
        Assert.True(result.Answered);
    }

    private static PapssParticipantBinding Binding() => new(LocalBic, "SO", ["USD"], "papss-callback-v1", ParticipantCallbackUrl);

    private static PayeeVerificationBuilder.Request Request() => new()
    {
        From = PapssSideBic,
        To = LocalBic,
        Alias = "0012030321735",
        Type = "BBAN",
        SIPSRequestId = "SIPS-VERIFY-ID-1"
    };

    private sealed class Scenario
    {
        public ParticipantCallbackContext Context { get; } = new();
        public string? CalledUrl { get; private set; }
        public CoreBankVerificationClient Client { get; }

        public Scenario()
        {
            var http = new Mock<SIPS.Core.Interfaces.IInterfaceHttpClient>();
            http.Setup(x => x.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
                .Returns((string url, Dictionary<string, string>? headers, StringContent content, CancellationToken ct) =>
                {
                    CalledUrl = url;
                    return Task.FromResult(new Response<JsonObject?>(new JsonObject { ["isVerified"] = true })
                    {
                        StatusCode = HttpStatusCode.OK,
                        IsSuccess = true
                    });
                });
            var callbackClient = new ParticipantCallbackClient(
                new CallbackClient(http.Object, NullLogger<CallbackClient>.Instance, new CorrelationService()),
                Context);

            Client = new CoreBankVerificationClient(
                new ISO20022Options { Verification = CoreBankUrl, Key = "key", Secret = "secret" },
                new JsonAdapterPassthrough(),
                new CallbackOrchestrator(),
                new CorrelationService(),
                callbackClient,
                Options.Create(new CoreOptions()),
                NullLogger<CoreBankVerificationClient>.Instance);
        }
    }

    // CB_VerificationRequest/CB_VerificationResponse mapping is irrelevant to the url-routing bug under
    // test, so this just round-trips the DTOs untouched.
    private sealed class JsonAdapterPassthrough : IJsonAdapter
    {
        public JsonObject Transform(JsonObject json, string mappingName) => json;
        public JsonObject Transform<T>(T localObject, string mappingName) => System.Text.Json.JsonSerializer.SerializeToNode(localObject)!.AsObject();
        public T ToObject<T>(JsonObject json) => System.Text.Json.JsonSerializer.Deserialize<T>(json)!;
    }
}
