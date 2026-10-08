using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SIPS.Adapter;
using SIPS.Connect.Controllers;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class PapssInboundPaymentAcceptanceTests
{
    [Fact]
    public void Host_registers_the_payment_requirement_on_the_authenticated_participant_context()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Xades:WithoutPKI"] = "true",
            ["Core:SAFExpression"] = "*/5 * * * *",
            ["Core:SAFTimeZoneInfo"] = "UTC"
        }).Build();
        SIPS.Connect.Config.DI.Register(services, configuration);
        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<IParticipantCallbackContext>();
        var payment = provider.GetRequiredService<IInboundPaymentContext>();
        Assert.Same(context, payment);
        Assert.False(payment.RequiresCoreBankAcceptance);
        using (context.Push(new("ZKBASOS0", "SO", ["USD"], "papss-callback-v1", "https://bank.test/callback")))
            Assert.True(payment.RequiresCoreBankAcceptance);
        Assert.False(payment.RequiresCoreBankAcceptance);
    }

    [Theory]
    [InlineData(false, "ACSC", "ACCP")]
    [InlineData(false, "ACCP", "ACCP")]
    [InlineData(false, "SUCC", "ACCP")]
    [InlineData(false, "RJCT", "RJCT")]
    [InlineData(false, "", "RJCT")]
    [InlineData(false, "PDNG", "RJCT")]
    [InlineData(true, "ACCP", "ACCP")]
    [InlineData(true, "RJCT", "RJCT")]
    public async Task Authenticated_papss_payment_gets_the_bank_decision_regardless_of_domestic_flag(
        bool includeOnListing, string bankStatus, string expected)
    {
        var scenario = new Scenario(includeOnListing, bankStatus);
        Assert.IsType<OkResult>(await scenario.ReceiveAsync(papss: true));
        var call = Assert.Single(scenario.Calls);
        Assert.Equal(Scenario.TransferUrl, call.Url);
        Assert.Equal("TX-PAPSS-ACCEPTANCE", call.Headers["X-Idempotency-Key"]);
        Assert.Equal("TX-PAPSS-ACCEPTANCE", call.Body["TxId"]!.GetValue<string>());
        Assert.Equal(12.5m, call.Body["Amount"]!.GetValue<decimal>());
        Assert.Equal(expected, Status(scenario.Record.Response!));
        var storedStatus = bankStatus == "RJCT" ? TransactionStatus.Failed
            : expected == "RJCT" ? TransactionStatus.CheckStatus : TransactionStatus.Pending;
        Assert.Equal(storedStatus, scenario.Record.Status);
        Assert.True(scenario.DecisionSawPersistedResponse);
        Assert.False(scenario.Context.RequiresCoreBankAcceptance);
        if (bankStatus == "RJCT")
            Assert.Contains("AM04", Encoding.UTF8.GetString(scenario.Record.Response!));
    }

    [Fact]
    public async Task Domestic_payment_still_skips_corebank_when_listing_flag_is_false()
    {
        var scenario = new Scenario(false, "RJCT");
        Assert.IsType<ContentResult>(await scenario.ReceiveAsync(papss: false));
        Assert.Empty(scenario.Calls);
        Assert.Equal("ACSC", Status(scenario.Record.Response!));
        scenario.Publisher.Verify(p => p.PersistAndSubmitAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Domestic_payment_still_consults_corebank_when_listing_flag_is_true()
    {
        var scenario = new Scenario(true, "RJCT");
        await scenario.ReceiveAsync(papss: false);
        Assert.Equal(Scenario.TransferUrl, Assert.Single(scenario.Calls).Url);
        Assert.Equal("RJCT", Status(scenario.Record.Response!));
        Assert.Equal(TransactionStatus.Failed, scenario.Record.Status);
    }

    [Fact]
    public async Task Http_failure_cannot_accept_a_papss_payment_even_with_a_success_body()
    {
        var scenario = new Scenario(false, "ACCP", HttpStatusCode.BadGateway);
        await scenario.ReceiveAsync(papss: true);
        Assert.Single(scenario.Calls);
        Assert.Equal("RJCT", Status(scenario.Record.Response!));
        Assert.Equal(TransactionStatus.CheckStatus, scenario.Record.Status);
    }

    [Fact]
    public async Task Empty_bank_response_cannot_accept_a_papss_payment()
    {
        var scenario = new Scenario(false, "ACCP") { EmptyResponse = true };
        await scenario.ReceiveAsync(papss: true);
        Assert.Single(scenario.Calls);
        Assert.Equal("RJCT", Status(scenario.Record.Response!));
        Assert.Equal(TransactionStatus.CheckStatus, scenario.Record.Status);
    }

    [Fact]
    public async Task Timeout_does_not_accept_and_records_reconciliation_state()
    {
        var scenario = new Scenario(false, "ACCP") { Timeout = true };
        await scenario.ReceiveAsync(papss: true);
        Assert.Single(scenario.Calls);
        Assert.Equal("RJCT", Status(scenario.Record.Response!));
        Assert.Equal(TransactionStatus.CheckStatus, scenario.Record.Status);
    }

    [Theory]
    [InlineData("ACCP")]
    [InlineData("RJCT")]
    public async Task Redelivery_reuses_the_persisted_bank_decision_without_another_transfer(string bankStatus)
    {
        var scenario = new Scenario(false, bankStatus);
        await scenario.ReceiveAsync(papss: true);
        var response = scenario.Record.Response!.ToArray();
        await scenario.ReceiveAsync(papss: true);
        Assert.Single(scenario.Calls);
        Assert.Equal(response, scenario.Record.Response);
    }


    private static string Status(byte[] xml) => XDocument.Parse(Encoding.UTF8.GetString(xml))
        .Descendants().Single(e => e.Name.LocalName == "TxSts").Value;

    private sealed class Scenario
    {
        public const string TransferUrl = "https://corebank.test/api/CB/Transfer";
        public ParticipantCallbackContext Context { get; } = new();
        public ISOMessage Record { get; } = new() { Id = 1, Status = TransactionStatus.Pending };
        public List<(string Url, Dictionary<string, string> Headers, JsonObject Body)> Calls { get; } = [];
        public Mock<IPapssPaymentDecisionPublisher> Publisher { get; } = new();
        public bool DecisionSawPersistedResponse { get; private set; }
        public bool EmptyResponse { get; init; }
        public bool Timeout { get; init; }
        private readonly IncomingTransactionHandler handler;
        private readonly string xml;
        private bool recorded;

        public Scenario(bool includeOnListing, string bankStatus, HttpStatusCode httpStatus = HttpStatusCode.OK)
        {
            var request = new PaymentRequestBuilder.Request
            {
                From = "PAPSS", To = "ZKBASOS0", BizMsgIdr = "PAPSS-INBOUND-PAYMENT", MsgId = "PAPSS-INBOUND-PAYMENT",
                MsgDefIdr = "pacs.008.001.10", CreDt = DateTime.UtcNow, TxId = "TX-PAPSS-ACCEPTANCE",
                EndToEndId = "E2E-PAPSS-ACCEPTANCE", Amount = 12.5m, Currency = "USD", LocalInstrument = "INST",
                CategoryPurpose = "CASH", Ustrd = "Payment", ClearingSystem = "FP", SettlementMethod = SIPS.ISO20022.Schemas.PRDocument.SettlementMethod1Code.CLRG,
                ChargeBearer = SIPS.ISO20022.Schemas.PRDocument.ChargeBearerType1Code.SLEV,
                Debtor = new Person { Name = "Sender", Account = "100", AccountType = "ACCT", AgentBIC = "PHBXSLFR", Issuer = "C" },
                Creditor = new Person { Name = "Recipient", Account = "200", AccountType = "ACCT", AgentBIC = "ZKBASOS0", Issuer = "C" }
            };
            xml = PaymentRequestBuilder.Build(request).document;
            var http = new Mock<SIPS.Core.Interfaces.IInterfaceHttpClient>();
            http.Setup(h => h.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
                .Returns(async (string url, Dictionary<string, string>? headers, StringContent content, CancellationToken ct) =>
                {
                    Calls.Add((url, new(headers!), JsonNode.Parse(await content.ReadAsStringAsync(ct))!.AsObject()));
                    if (Timeout) throw new TaskCanceledException("bank timeout");
                    return new Response<JsonObject?>(EmptyResponse ? null : new JsonObject { ["Status"] = bankStatus, ["Reason"] = "AM04" })
                        { StatusCode = httpStatus };
                });
            var correlation = new CorrelationService();
            var callback = new ParticipantCallbackClient(new CallbackClient(http.Object, NullLogger<CallbackClient>.Instance, correlation), Context);
            var signature = new Mock<ISignatureService>();
            signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((true, "ok"));
            var iso = new Mock<IISOMessageService>();
            iso.Setup(s => s.TryRecordIncomingTransactionAsync(It.IsAny<PaymentRequestBuilder.Request>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { var isNew = !recorded; recorded = true; return ((ISOMessage?)Record, isNew); });
            iso.Setup(s => s.PersistTransactionResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<TransactionStatus>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((ISOMessage record, TransactionStatus status, string reason, string? info, string response, string txId, string endToEndId, CancellationToken ct) =>
                {
                    record.Status = status; record.Response = Encoding.UTF8.GetBytes(response);
                }).Returns(Task.CompletedTask);
            var signer = new Mock<INativeSigner>();
            signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns((string body, string algorithm) => body);
            handler = new IncomingTransactionHandler(
                new ISO20022Options { Transfer = TransferUrl, Key = "key", Secret = "secret" },
                NullLogger<IncomingTransactionHandler>.Instance, signer.Object, new PassthroughAdapter(),
                new PaymentRequestParser(), callback, new ResponseFactory(), correlation,
                new InboundMessageService(signature.Object, Context), new CallbackOrchestrator(), iso.Object,
                Options.Create(new CoreOptions { IncludeCoreBankOnListing = includeOnListing }), Context);
            Publisher.Setup(p => p.PersistAndSubmitAsync(It.IsAny<PapssParticipantBinding>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => DecisionSawPersistedResponse = Record.Response is not null).Returns(Task.CompletedTask);
        }

        public async Task<ActionResult> ReceiveAsync(bool papss)
        {
            var guard = new Mock<IPapssCallbackGuard>();
            guard.Setup(g => g.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(papss ? new PapssParticipantBinding("ZKBASOS0", "SO", ["USD"], "papss-callback-v1", "https://bank.test/CompletionNotification") : null);
            var incoming = new Mock<IIncoming>();
            incoming.Setup(i => i.Handle(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string message, CancellationToken ct) => new ValueTask<string>(handler.HandleAsync(message, ct)));
            var controller = new IncomingController(incoming.Object, guard.Object, Publisher.Object,
                Mock.Of<IPapssInboundVerificationService>(), Context, NullLogger<IncomingController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return await controller.Post(CancellationToken.None);
        }

    }

    private sealed class PassthroughAdapter : IJsonAdapter
    {
        public JsonObject Transform(JsonObject json, string endpointName) => json;
        public JsonObject Transform<T>(T value, string endpointName) => JsonSerializer.SerializeToNode(value)!.AsObject();
        public T ToObject<T>(JsonObject json) => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
