using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using SIPS.Core.Services;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Options;
using SIPS.Adapter;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.Core.Services.Implementations;
namespace SIPS.Core.Tests.Tests;

public class IncomingPaymentStatusReportHandler_Tests
{
    private static string SamplePacs002WithTxId(string txId) => $"<Doc><TxId>{txId}</TxId></Doc>";

    private static IncomingPaymentStatusReportHandler BuildHandler(
        ISignatureService sig,
        IPersistenceGateway pg,
        IJsonAdapter jsonAdapter,
        ICallbackClient callback,
        INativeSigner signer)
    {
        var options = new ISO20022Options { Status = "http://cb/status", Key = "k", Secret = "s" };
        var logger = Mock.Of<ILogger<IncomingPaymentStatusReportHandler>>();
        var responseFactory = new ResponseFactory();
        var correlation = new CorrelationService();
        var parser = new PaymentStatusReportParser();

        var inbound = new InboundMessageService(sig);
        var callbacks = new CallbackOrchestrator();
        var isoService = new ISOMessageService(pg);
        var core = Microsoft.Extensions.Options.Options.Create(new SIPS.Core.Options.CoreOptions());
        return new IncomingPaymentStatusReportHandler(
            options,
            logger,
            signer,
            jsonAdapter,
            parser,
            callback,
            responseFactory,
            pg,
            correlation,
            inbound,
            callbacks,
            isoService,
            core);
    }

    [Fact]
    public async Task NonPending_DB_Maps_To_ACSC_Without_CB_Call()
    {
        var sig = new Mock<ISignatureService>();
        sig.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((true, "ok"));

        var iso = new ISOMessage
        {
            Status = TransactionStatus.Success,
            FromBIC = "FROM",
            ToBIC = "TO",
            TxId = "T1",
            EndToEndId = "E1",
            BizMsgIdr = "B",
            MsgDefIdr = "pacs.008.001.10",
            MsgId = "M"
        };
        var pg = new Mock<IPersistenceGateway>();
        pg.Setup(p => p.GetISOMessageByTxIdAsync("T1", It.IsAny<CancellationToken>())).ReturnsAsync(iso);
        pg.Setup(p => p.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ISOMessageStatus { ISOMessage = iso });

        var jsonAdapter = new Mock<IJsonAdapter>();
        var callback = new Mock<ICallbackClient>(MockBehavior.Strict);
        var signer = new Mock<INativeSigner>();
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
              .Returns<string, string>((m, alg) => m);

        var handler = BuildHandler(sig.Object, pg.Object, jsonAdapter.Object, callback.Object, signer.Object);

        var xml = SamplePacs002WithTxId("T1");
        var rsp = await handler.HandleAsync(xml, CancellationToken.None);

        Assert.Contains("T1", rsp);
        Assert.Contains("ACSC", rsp);
        callback.Verify(c => c.SendAsync(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Pending_DB_Calls_CB_And_Maps_Response()
    {
        var sig = new Mock<ISignatureService>();
        sig.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((true, "ok"));

        var iso = new ISOMessage
        {
            Status = TransactionStatus.Pending,
            FromBIC = "FROM",
            ToBIC = "TO",
            TxId = "T2",
            EndToEndId = "E2",
            BizMsgIdr = "B",
            MsgDefIdr = "pacs.008.001.10",
            MsgId = "M"
        };
        var pg = new Mock<IPersistenceGateway>();
        pg.Setup(p => p.GetISOMessageByTxIdAsync("T2", It.IsAny<CancellationToken>())).ReturnsAsync(iso);
        pg.Setup(p => p.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ISOMessageStatus { ISOMessage = iso });
        pg.Setup(p => p.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ISOMessageStatus { ISOMessage = iso });

        var jsonAdapter = new Mock<IJsonAdapter>();
        jsonAdapter.Setup(j => j.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
                   .Returns(new JsonObject());
        jsonAdapter.Setup(j => j.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
                   .Returns(new CBPaymentStatusResponseDto { Status = "ACSC", TxId = "T2" });

        var callback = new Mock<ICallbackClient>();
        callback.Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync(SIPS.ISO20022.Models.DTOs.Response<JsonObject?>.Success(new JsonObject()))
                .Verifiable();

        var signer = new Mock<INativeSigner>();
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
              .Returns<string, string>((m, alg) => m);

        var handler = BuildHandler(sig.Object, pg.Object, jsonAdapter.Object, callback.Object, signer.Object);
        var xml = SamplePacs002WithTxId("T2");
        var rsp = await handler.HandleAsync(xml, CancellationToken.None);

        Assert.Contains("T2", rsp);
        Assert.Contains("ACSC", rsp);
        callback.Verify();
    }

    [Fact]
    public async Task Signature_Failure_Returns_AdminMessage()
    {
        var sig = new Mock<ISignatureService>();
        sig.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((false, "bad"));

        var pg = new Mock<IPersistenceGateway>();
        var jsonAdapter = new Mock<IJsonAdapter>();
        var callback = new Mock<ICallbackClient>();
        var signer = new Mock<INativeSigner>();
        signer.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
              .Returns<string, string>((m, alg) => m);

        var handler = BuildHandler(sig.Object, pg.Object, jsonAdapter.Object, callback.Object, signer.Object);
        var rsp = await handler.HandleAsync(SamplePacs002WithTxId("T3"), CancellationToken.None);

        Assert.Contains("admi.002", rsp);
    }
}
