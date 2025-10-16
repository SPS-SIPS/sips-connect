using System;
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
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class IncomingTransactionHandler_Callback_Tests
{
    private static (IncomingTransactionHandler sut, Mock<IIncomingRecorder> rec, Mock<IInterfaceHttpClient> http, Mock<IJsonAdapter> adapter)
        CreateSut(Func<Response<JsonObject?>> httpResultFactory)
    {
        var options = new ISO20022Options
        {
            Transfer = "https://example.test/transfer",
            Key = "key",
            Secret = "secret"
        };

        var logger = Mock.Of<ILogger<IncomingTransactionHandler>>();

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
        adapter.Setup(a => a.ToObject<PaymentResponseDto>(It.IsAny<JsonObject>()))
               .Returns(new PaymentResponseDto { Status = "RJCT", Reason = "X", AdditionalInfo = "", AcceptanceDate = DateTime.UtcNow, TxId = "TX" });

        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.ISOMessageAsync(It.IsAny<SIPS.PostgreSQL.Models.ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SIPS.PostgreSQL.Models.ISOMessage m, CancellationToken _) => m);
        recorder.Setup(r => r.ISOMessageResponseAsync(It.IsAny<SIPS.PostgreSQL.Models.ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SIPS.PostgreSQL.Models.ISOMessage m, CancellationToken _) => m);

        // new deps
        var correlation = new CorrelationService();
        var responses = new ResponseFactory();
        var persistence = new PersistenceGateway(recorder.Object);
        var cbLogger = Mock.Of<Microsoft.Extensions.Logging.ILogger<CallbackClient>>();
        var callback = new CallbackClient(http.Object, cbLogger, correlation);
        var signature = new Mock<ISignatureService>();
        signature.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((true, "ok"));
        var parser = new PaymentRequestParser();

        var sut = new IncomingTransactionHandler(options, logger, http.Object, signer, verifier.Object, adapter.Object, recorder.Object,
            signature.Object, parser, callback, responses, persistence, correlation);
        return (sut, recorder, http, adapter);
    }

    private static string MakeValidPaymentRequestXml()
    {
        var req = new PaymentRequestBuilder.Request
        {
            From = "BICA",
            To = "BICB",
            BizMsgIdr = "",
            MsgDefIdr = "",
            CreDt = DateTime.UtcNow,
            SettlementMethod = SIPS.ISO20022.Schemas.PRDocument.SettlementMethod1Code.CLRG,
            ClearingSystem = "FP",
            LocalInstrument = "LI",
            CategoryPurpose = "CP",
            TxId = "TX",
            EndToEndId = "E2E",
            Amount = 1,
            Currency = "USD",
            ChargeBearer = SIPS.ISO20022.Schemas.PRDocument.ChargeBearerType1Code.SLEV,
            Debtor = new SIPS.ISO20022.Models.Person { Name = "A", Account = "A1", AccountType = "CHK", AgentBIC = "AGT1", Issuer = "C" },
            Creditor = new SIPS.ISO20022.Models.Person { Name = "B", Account = "B1", AccountType = "SAV", AgentBIC = "AGT2", Issuer = "C" },
            Ustrd = "U"
        };
        var (xml, _, _, _) = PaymentRequestBuilder.Build(req);
        return xml;
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpIsNotOk()
    {
        var xml = MakeValidPaymentRequestXml();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.BadGateway });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<SIPS.PostgreSQL.Models.ISOMessage>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_PersistsFailed_WhenHttpOkButNullData()
    {
        var xml = MakeValidPaymentRequestXml();
        var (sut, recorder, _, _) = CreateSut(() => new Response<JsonObject?>(null) { StatusCode = HttpStatusCode.OK });

        var rsp = await sut.HandleAsync(xml, CancellationToken.None);

        rsp.Should().NotBeNullOrWhiteSpace();
        recorder.Verify(r => r.ISOMessageResponseAsync(It.Is<SIPS.PostgreSQL.Models.ISOMessage>(m => m.Status == TransactionStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }
}
