using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Net.Http;
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

public class IncomingTransactionStatusHandler_HappyPath_Tests
{
    private static (IncomingTransactionStatusHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http, Mock<IJsonAdapter> adapter, Mock<INativeSigner> signer)
        CreateSut()
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
            .ReturnsAsync(new SIPS.ISO20022.Models.DTOs.Response<JsonObject?>(new JsonObject { ["ok"] = true })
            {
                StatusCode = System.Net.HttpStatusCode.OK
            });

        var signer = new Mock<INativeSigner>();
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns("signed-envelope");

        var adapter = new Mock<IJsonAdapter>();
        adapter.Setup(a => a.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });
        adapter.Setup(a => a.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
               .Returns(new CBPaymentStatusResponseDto
               {
                   Status = SIPS.Core.Constants.ACSC,
                   Reason = string.Empty,
                   AdditionalInfo = string.Empty,
                   AcceptanceDate = DateTime.UtcNow,
                   TxId = "TX1234",
                   FromBIC = "BICA",
                   ToBIC = "BICB",
                   BizMsgIdr = "BIZ",
                   MsgId = "MSG",
                   ClearingSystem = "FP",
                   MsgDefIdr = "CTST",
                   Date = DateTime.UtcNow,
                   LocalInstrument = "LI",
                   CategoryPurpose = "CP",
                   EndToEndId = "E2E1234",
                   Amount = 100,
                   Currency = "USD",
                   DebtorName = "Alice",
                   DebtorAccount = "A1",
                   DebtorAccountType = "CHK",
                   DebtorAgentBIC = "AGT1",
                   DebtorIssuer = "C",
                   CreditorName = "Bob",
                   CreditorAccount = "B1",
                   CreditorAccountType = "SAV",
                   CreditorAgentBIC = "AGT2",
                   CreditorIssuer = "C",
                   RemittanceInformation = "payment"
               });

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.GetISOMessageByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessage { Id = 1, Status = TransactionStatus.Pending });
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

        var sut = new IncomingTransactionStatusHandler(options, logger, http.Object, signer.Object, verifier.Object, adapter.Object, recorder.Object,
            signature.Object, parser, callback, responses, persistence, correlation);
        return (sut, recorder, http, adapter, signer);
    }

    [Fact]
    public async Task HandleAsync_ReturnsSignedEnvelope_AndPersistsStatus()
    {
        var xml = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "pacs.002.xml"));

        var (sut, recorder, _, _, _) = CreateSut();

        // Act
        var result = await sut.HandleAsync(xml, CancellationToken.None);

        // Assert
        result.Should().Be("signed-envelope");
        recorder.Verify(r => r.ISOMessageStatusResponseAsync(It.Is<ISOMessageStatus>(m => m.Status == TransactionStatus.Success), It.IsAny<CancellationToken>()), Times.Once);
    }
}
