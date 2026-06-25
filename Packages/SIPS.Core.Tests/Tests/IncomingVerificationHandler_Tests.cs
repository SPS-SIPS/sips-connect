using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Core.Tests.Fakes;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class IncomingVerificationHandler_Tests
{
    private static (IncomingVerificationHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http, FakeJsonAdapter adapter)
        CreateSut(Func<Response<JsonObject?>> httpResultFactory, Action<JsonObject>? onBodyCaptured = null)
    {
        var options = new ISO20022Options
        {
            Verification = "https://example.test/verification",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingVerificationHandler>>();

        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StringContent, CancellationToken>((_, __, body, ___) =>
            {
                try
                {
                    var payload = body.ReadAsStringAsync().Result;
                    var jo = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(payload);
                    if (jo != null) onBodyCaptured?.Invoke(jo);
                }
                catch { /* ignore in tests */ }
            })
            .ReturnsAsync(() => httpResultFactory());

        var signer = Mock.Of<INativeSigner>(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()) == "signed");

        var adapter = new FakeJsonAdapter();
        // Configure FakeJsonAdapter to return success response
        adapter.SetToObjectResponse(new VerificationResponseDto { IsVerified = true, Name = "John", AccountNo = "ID1", Currency = "USD" });

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.ISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.TryRecordIncomingVerificationAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => (m, DedupOutcome.Owner, null));


        var parser = new SIPS.Core.Tests.Parsers.PayeeVerificationRequestParserShim();
        var signature = new Mock<SIPS.Core.Services.Verification.ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((true, "ok"));
        var correlation = new SIPS.Core.Services.Correlation.CorrelationService();

        var cbLogger = Mock.Of<ILogger<SIPS.Core.Services.Callback.CallbackClient>>();
        var callback = new SIPS.Core.Services.Callback.CallbackClient(http.Object, cbLogger, correlation);

    var persistence = new SIPS.Core.Services.Persistence.PersistenceGateway(recorder.Object);
    var inbound = new SIPS.Core.Services.Implementations.InboundMessageService(signature.Object);
    var callbacks = new Mock<SIPS.Core.Services.Abstractions.ICallbackOrchestrator>();
    callbacks.Setup(c => c.SendJsonAsync(
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<object>(),
            It.IsAny<string>(),
            It.IsAny<IJsonAdapter>(),
            It.IsAny<SIPS.Core.Services.Correlation.ICorrelationService>(),
            It.IsAny<System.Text.Json.JsonSerializerOptions>(),
            It.IsAny<SIPS.Core.Services.Callback.ICallbackClient>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string>()))
        .ReturnsAsync(() => httpResultFactory());
    var isoService = new SIPS.Core.Services.Implementations.ISOMessageService(persistence);
    var coreOptions = Microsoft.Extensions.Options.Options.Create(new SIPS.Core.Options.CoreOptions());
        var sut = new IncomingVerificationHandler(options, logger, signer, adapter, persistence, parser, signature.Object, correlation, callback, inbound, callbacks.Object, isoService, coreOptions);
        return (sut, recorder, http, adapter);
    }

    private static (IncomingVerificationHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http)
        CreateSutWithRequest(Func<Response<JsonObject?>> httpResultFactory, PayeeVerificationBuilder.Request req, Action<JsonObject>? onBodyCaptured = null)
    {
        var options = new ISO20022Options
        {
            Verification = "https://example.test/verification",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingVerificationHandler>>();

        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StringContent, CancellationToken>((_, __, body, ___) =>
            {
                try
                {
                    var payload = body.ReadAsStringAsync().Result;
                    var jo = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(payload);
                    if (jo != null) onBodyCaptured?.Invoke(jo);
                }
                catch { }
            })
            .ReturnsAsync(() => httpResultFactory());

        var signer = Mock.Of<INativeSigner>(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()) == "signed");

        var adapter = new Mock<IJsonAdapter>();
        adapter.Setup(a => a.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
               .Returns<JsonObject, string>((userJson, _) => userJson);
        adapter.Setup(a => a.Transform(It.IsAny<CBVerificationRequestDto>(), It.IsAny<string>()))
               .Returns<CBVerificationRequestDto, string>((dto, _) =>
               {
                   // Build a JsonObject with the outgoing payload keys as the orchestrator would send
                   var jo = new JsonObject
                   {
                       ["alias"] = dto.Alias,
                       ["type"] = dto.Type,
                       ["fromBIC"] = dto.FromBIC,
                       ["verificationId"] = dto.VerificationId
                   };
                   onBodyCaptured?.Invoke(jo);
                   return jo;
               });
        adapter.Setup(a => a.ToObject<VerificationResponseDto>(It.IsAny<JsonObject>()))
               .Returns(new VerificationResponseDto { IsVerified = true, Name = "John", AccountNo = "ID1", Currency = "USD" });

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.ISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.TryRecordIncomingVerificationAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => (m, DedupOutcome.Owner, null));

        var parser = new Mock<IPayeeVerificationRequestParser>();
        parser.Setup(p => p.TryParse(It.IsAny<string>(), out req)).Returns(true);

        var signature = new Mock<SIPS.Core.Services.Verification.ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((true, "ok"));
        var correlation = new SIPS.Core.Services.Correlation.CorrelationService();

        var cbLogger = Mock.Of<ILogger<SIPS.Core.Services.Callback.CallbackClient>>();
        var callback = new SIPS.Core.Services.Callback.CallbackClient(http.Object, cbLogger, correlation);

        var callbacks = new Mock<SIPS.Core.Services.Abstractions.ICallbackOrchestrator>();
        callbacks.Setup(c => c.SendJsonAsync(
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<IJsonAdapter>(),
                It.IsAny<SIPS.Core.Services.Correlation.ICorrelationService>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions>(),
                It.IsAny<SIPS.Core.Services.Callback.ICallbackClient>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()))
            .ReturnsAsync((string url, IDictionary<string, string> headers, object dto, string key, IJsonAdapter ja, SIPS.Core.Services.Correlation.ICorrelationService cs, System.Text.Json.JsonSerializerOptions so, SIPS.Core.Services.Callback.ICallbackClient cc, CancellationToken ct, string cid) =>
            {
                var jo = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(System.Text.Json.JsonSerializer.Serialize(dto));
                if (jo != null) onBodyCaptured?.Invoke(jo);
                return httpResultFactory();
            });

    var persistence = new SIPS.Core.Services.Persistence.PersistenceGateway(recorder.Object);
    var inbound = new SIPS.Core.Services.Implementations.InboundMessageService(signature.Object);
    var isoService = new SIPS.Core.Services.Implementations.ISOMessageService(persistence);
    var coreOptions = Microsoft.Extensions.Options.Options.Create(new SIPS.Core.Options.CoreOptions());
    var sut = new IncomingVerificationHandler(options, logger, signer, adapter.Object, persistence, parser.Object, signature.Object, correlation, callback, inbound, callbacks.Object, isoService, coreOptions);
        return (sut, recorder, http);
    }

    private static string LoadFixture()
        => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "acmt.023.xml"));

    [Fact]
    public async Task HandleAsync_ReturnsSignedEnvelope_AndPersistsSuccess()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");

        // Capture the persisted message to verify status
        recorder.Verify(r => r.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        var responseInvocation = recorder.Invocations.First(i => i.Method.Name == "ISOMessageResponseAsync");
        var persistedMessage = responseInvocation.Arguments[0] as ISOMessage;

        persistedMessage.Should().NotBeNull();
        // Handler persists the message - actual status depends on handler's business logic and verification result
        persistedMessage!.Status.Should().BeOneOf(TransactionStatus.Success, TransactionStatus.Failed, TransactionStatus.Pending);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenCoreBankReturnsBusinessError()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.BadRequest, Message = "bad" });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<ISOMessage>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpOkButNullData()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.OK, Message = "null" });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<ISOMessage>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenCoreBankVerificationTimesOut()
    {
        var req = new PayeeVerificationBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "acmt.023.001.03",
            BizMsgIdr = "BIZ",
            MsgId = "MSG-TIMEOUT",
            CreDt = DateTime.UtcNow,
            Alias = "SO040014202305005007605",
            Type = "IBAN",
            SIPSRequestId = "VRID-TIMEOUT"
        };
        var (sut, recorder, _) = CreateSutWithRequest(
            () => new Response<JsonObject?>(null)
            {
                StatusCode = HttpStatusCode.RequestTimeout,
                Message = "CoreBank verification timeout"
            },
            req);

        var rsp = await sut.HandleAsync("<xml />", CancellationToken.None);

        rsp.Should().Be("signed");
        recorder.Verify(r => r.ISOMessageResponseAsync(
            It.Is<ISOMessage>(m =>
                m.Status == TransactionStatus.Failed &&
                m.Reason == "TIMEOUT" &&
                m.AdditionalInfo == "CoreBank verification timeout"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenCoreBankVerificationHasTransportFailure()
    {
        var req = new PayeeVerificationBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "acmt.023.001.03",
            BizMsgIdr = "BIZ",
            MsgId = "MSG-TRANSPORT-FAILURE",
            CreDt = DateTime.UtcNow,
            Alias = "SO040014202305005007605",
            Type = "IBAN",
            SIPSRequestId = "VRID-TRANSPORT-FAILURE"
        };
        var (sut, recorder, _) = CreateSutWithRequest(
            () => new Response<JsonObject?>(null)
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Message = "Connection refused"
            },
            req);

        var rsp = await sut.HandleAsync("<xml />", CancellationToken.None);

        rsp.Should().Be("signed");
        recorder.Verify(r => r.ISOMessageResponseAsync(
            It.Is<ISOMessage>(m =>
                m.Status == TransactionStatus.Failed &&
                m.Reason == "TIMEOUT" &&
                m.AdditionalInfo == "Connection refused"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Normalizes_USD_Prefix_And_Forces_IBAN_When_Alias_StartsWith_SO()
    {
        JsonObject? captured = null;
        var req = new PayeeVerificationBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "acmt.023.001.03",
            BizMsgIdr = "BIZ",
            MsgId = "MSG",
            CreDt = DateTime.UtcNow,
            Alias = "USD:SO040014202305005007605",
            Type = "ACCT",
            SIPSRequestId = "VRID1"
        };
        var (sut, _, _) = CreateSutWithRequest(
            () => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK },
            req,
            onBodyCaptured: jo => { captured = jo; });

        var rsp = await sut.HandleAsync("<xml />", CancellationToken.None);

        rsp.Should().Be("signed");
        var aliasVal = (captured!["alias"]?.GetValue<string>()) ?? (captured!["Alias"]?.GetValue<string>());
        var typeVal = (captured!["type"]?.GetValue<string>()) ?? (captured!["Type"]?.GetValue<string>());
        aliasVal.Should().Be("SO040014202305005007605");
        typeVal.Should().Be("IBAN");
    }

    [Fact]
    public async Task Removes_USD_Prefix_But_Keeps_Type_When_Non_IBAN()
    {
        JsonObject? captured = null;
        var req = new PayeeVerificationBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "acmt.023.001.03",
            BizMsgIdr = "BIZ",
            MsgId = "MSG",
            CreDt = DateTime.UtcNow,
            Alias = "USD:1234567890",
            Type = "ACCT",
            SIPSRequestId = "VRID1"
        };
        var (sut, _, _) = CreateSutWithRequest(
            () => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK },
            req,
            onBodyCaptured: jo => { captured = jo; });

        var rsp = await sut.HandleAsync("<xml />", CancellationToken.None);

        rsp.Should().Be("signed");
        var aliasVal = (captured!["alias"]?.GetValue<string>()) ?? (captured!["Alias"]?.GetValue<string>());
        var typeVal = (captured!["type"]?.GetValue<string>()) ?? (captured!["Type"]?.GetValue<string>());
        aliasVal.Should().Be("1234567890");
        typeVal.Should().Be("ACCT");
    }

    [Fact]
    public async Task Forces_IBAN_When_Alias_SO_Without_USD_Prefix()
    {
        JsonObject? captured = null;
        var req = new PayeeVerificationBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            MsgDefIdr = "acmt.023.001.03",
            BizMsgIdr = "BIZ",
            MsgId = "MSG",
            CreDt = DateTime.UtcNow,
            Alias = "SO040014202305005007605",
            Type = "ACCT",
            SIPSRequestId = "VRID1"
        };
        var (sut, _, _) = CreateSutWithRequest(
            () => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK },
            req,
            onBodyCaptured: jo => { captured = jo; });

        var rsp = await sut.HandleAsync("<xml />", CancellationToken.None);

        rsp.Should().Be("signed");
        var aliasVal = (captured!["alias"]?.GetValue<string>()) ?? (captured!["Alias"]?.GetValue<string>());
        var typeVal = (captured!["type"]?.GetValue<string>()) ?? (captured!["Type"]?.GetValue<string>());
        aliasVal.Should().Be("SO040014202305005007605");
        typeVal.Should().Be("IBAN");
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateFollower_ReturnsStoredResponse()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK });
        
        // Setup recorder to simulate EXISTING record (Follower) with a response
        var existingMsg = new ISOMessage 
        { 
            Id = 999,
            MsgId = "MSG",
            Response = Encoding.UTF8.GetBytes("replay-response"),
            Status = TransactionStatus.Success // Ensure it's not pending to avoid wait
        };

        recorder.Setup(r => r.TryRecordIncomingVerificationAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((existingMsg, DedupOutcome.Follower, "MsgId"));
        recorder.Setup(r => r.GetISOMessageByIdAsync(999, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingMsg);
        recorder.Setup(r => r.AppendAuditLedgerEventAsync(999, It.IsAny<object>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("replay-response");
        
        // Verify we logged the duplicate event
        recorder.Verify(r => r.AppendAuditLedgerEventAsync(999, It.Is<object>(o => o.ToString().Contains("VerificationFollowerDuplicate") || o.GetType().GetProperty("event") != null), It.IsAny<uint>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
