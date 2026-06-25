using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Adapter;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using static SIPS.Core.Constants;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class IncomingReturnTransactionHandler_Tests
{
    private static (IncomingReturnTransactionHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http, Mock<IJsonAdapter> adapter, List<ISOMessage> persistedMessages)
        CreateSut(
            Func<Response<JsonObject?>> httpResultFactory,
            bool includeCoreBankOnListing = false,
            string coreBankStatus = RJCT)
    {
        var options = new ISO20022Options
        {
            Return = "https://example.test/return",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingReturnTransactionHandler>>();

        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => httpResultFactory());

        var signer = Mock.Of<INativeSigner>(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()) == "signed");

        var adapter = new Mock<IJsonAdapter>();
        adapter.Setup(a => a.Transform(It.IsAny<object>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });
        adapter.Setup(a => a.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });
        adapter.Setup(a => a.ToObject<CBReturnResponseDto>(It.IsAny<JsonObject>()))
               .Returns(new CBReturnResponseDto { Status = coreBankStatus, Reason = "MISS", AdditionalInfo = "", OrgnlTxId = "TX1" });

        var persistedMessages = new List<ISOMessage>();
        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.ISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .Callback<ISOMessage, CancellationToken>((m, _) => persistedMessages.Add(m))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.GetISOMessageWithTransactionsByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessage
                {
                    MessageType = ISOMessageType.TransactionRequest,
                    Status = TransactionStatus.Success,
                    TxId = "AGROSOS0528910638962089436554484",
                    Transactions =
                    {
                        new Transaction { TxId = "AGROSOS0528910638962089436554484", EndToEndId = "ZKBASOS009957524", Amount = 3000m, Currency = "USD", LocalInstrument = "FP", CategoryPurpose = "TRF", FromBIC = "BICB" }
                    }
                });
        recorder.Setup(r => r.TryRecordIncomingReturnAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => (m, true));

        var signature = new Mock<SIPS.Core.Services.Verification.ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((true, "ok"));
    var correlation = new SIPS.Core.Services.Correlation.CorrelationService();
    var cbLogger = Mock.Of<ILogger<SIPS.Core.Services.Callback.CallbackClient>>();
    var callback = new SIPS.Core.Services.Callback.CallbackClient(http.Object, cbLogger, correlation);
    var callbacks = new CallbackOrchestrator();
    var parser = new SIPS.Core.Tests.Parsers.ReturnPaymentRequestParserShim();
    var persistence = new SIPS.Core.Services.Persistence.PersistenceGateway(recorder.Object);
    var inbound = new InboundMessageService(signature.Object);
    var isoService = new ISOMessageService(persistence);
        var coreOptions = Microsoft.Extensions.Options.Options.Create(new SIPS.Core.Options.CoreOptions
        {
            IncludeCoreBankOnListing = includeCoreBankOnListing
        });

        var sut = new IncomingReturnTransactionHandler(options, logger, signer, adapter.Object, recorder.Object, signature.Object, persistence, correlation, callback, parser, inbound, callbacks, isoService, coreOptions);
        return (sut, recorder, http, adapter, persistedMessages);
    }

    private static string LoadFixture()
        => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "pacs.004.xml"));

    [Fact]
    public async Task HandleAsync_ReturnsSignedEnvelope_AndPersists()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _, _) = CreateSut(() => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<ISOMessage>(m => m.Status == TransactionStatus.Success || m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutCoreBankOnListing_PersistsAcceptedReturnAsReadyForReturn()
    {
        var xml = LoadFixture();
        var (sut, _, _, _, persistedMessages) = CreateSut(
            () => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK },
            includeCoreBankOnListing: false);

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");
        persistedMessages.Select(m => $"{m.MessageType}:{m.Status}:{m.Reason}")
            .Should()
            .ContainSingle("ReturnRequest:ReadyForReturn:Return accepted - awaiting confirm");
    }

    [Fact]
    public async Task HandleAsync_WithCoreBankOnListing_AcceptsReturnOnlyOnExplicitCoreBankSuccess()
    {
        var xml = LoadFixture();
        var (sut, _, _, _, persistedMessages) = CreateSut(
            () => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK },
            includeCoreBankOnListing: true,
            coreBankStatus: SUCC);

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");
        persistedMessages.Select(m => $"{m.MessageType}:{m.Status}:{m.Reason}")
            .Should()
            .ContainSingle("ReturnRequest:Success:Return accepted by CoreBank - awaiting confirm");
    }

    [Fact]
    public async Task HandleAsync_WithCoreBankOnListing_RejectsReturnOnEmptyCoreBankStatus()
    {
        var xml = LoadFixture();
        var (sut, _, _, _, persistedMessages) = CreateSut(
            () => new Response<JsonObject?>(new JsonObject { ["ok"] = true }) { StatusCode = HttpStatusCode.OK },
            includeCoreBankOnListing: true,
            coreBankStatus: string.Empty);

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");
        persistedMessages.Select(m => $"{m.MessageType}:{m.Status}:{m.Reason}")
            .Should()
            .ContainSingle("ReturnRequest:Failed:MS03");
    }

    [Fact]
    public async Task HandleAsync_WithCoreBankOnListing_RejectsReturnOnEmptyCoreBankResponse()
    {
        var xml = LoadFixture();
        var (sut, _, _, _, persistedMessages) = CreateSut(
            () => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.OK },
            includeCoreBankOnListing: true,
            coreBankStatus: SUCC);

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().Be("signed");
        persistedMessages.Select(m => $"{m.MessageType}:{m.Status}:{m.Reason}")
            .Should()
            .ContainSingle("ReturnRequest:Failed:MS03");
    }
}
