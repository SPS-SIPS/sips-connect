using System;
using System.Collections.Generic;
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
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class IncomingTransactionStatusHandler_Callback_Tests
{
    private static (IncomingTransactionStatusHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http, Mock<IJsonAdapter> adapter)
        CreateSut(Func<SIPS.ISO20022.Models.DTOs.Response<JsonObject?>> httpResultFactory)
    {
        var options = new ISO20022Options
        {
            Status = "https://example.test/status",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingTransactionStatusHandler>>();

        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(v => v.VerifySignature(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, new VerboseResult { SignatureStatus = "ok" }));

        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => httpResultFactory());

        var signer = Mock.Of<INativeSigner>(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()) == "signed");

        var adapter = new Mock<IJsonAdapter>();
        adapter.Setup(a => a.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });
        adapter.Setup(a => a.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
               .Returns(new CBPaymentStatusResponseDto { Status = "RJCT", Reason = "X", TxId = "TX" });

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.GetISOMessageByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessage { Id = 10, Status = TransactionStatus.Pending });
        recorder.Setup(r => r.ISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessageStatus s, CancellationToken _) => { s.ISOMessage = new ISOMessage(); return s; });
        recorder.Setup(r => r.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessageStatus s, CancellationToken _) => s);

        // new deps
        var correlation = new CorrelationService();
        var responses = new ResponseFactory();
        var persistence = new PersistenceGateway(recorder.Object);
        var cbLogger = Mock.Of<ILogger<CallbackClient>>();
        var callback = new CallbackClient(http.Object, cbLogger, correlation);
        var signature = new Mock<ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((true, "ok"));
        var parser = new SIPS.Core.Tests.Parsers.PaymentStatusRequestParserShim();

        var sut = new IncomingTransactionStatusHandler(options, logger, http.Object, signer, verifier.Object, adapter.Object, recorder.Object,
            signature.Object, parser, callback, responses, persistence, correlation);
        return (sut, recorder, http, adapter);
    }

    private static string LoadFixture()
        => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "pacs.002.xml"));

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpIsNotOk()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new SIPS.ISO20022.Models.DTOs.Response<JsonObject?>(null) { StatusCode = HttpStatusCode.BadGateway });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageStatusResponseAsync(It.Is<ISOMessageStatus>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpOkButNullData()
    {
        var xml = LoadFixture();
        var (sut, recorder, _, _) = CreateSut(() => new SIPS.ISO20022.Models.DTOs.Response<JsonObject?>(null) { StatusCode = HttpStatusCode.OK });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageStatusResponseAsync(It.Is<ISOMessageStatus>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
