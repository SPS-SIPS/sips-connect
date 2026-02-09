
using System;
using SIPS.Adapter;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Abstractions;
using SIPS.ISO20022.Helpers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Interfaces; 
using SIPS.Core.Services.Verification; 
using SIPS.Core.Services.Correlation; 
using SIPS.Core.Services.ISOParsers;
using static SIPS.Core.Constants; 
using System.Net.Http;
using System.Net;

namespace SIPS.Core.Tests.Verification
{
    public class VerificationLoopbackTests
    {
        private Mock<IPersistenceGateway> _mockPersistence;
        private Mock<IInboundMessageService> _mockInbound;
        private Mock<IISOMessageService> _mockIsoService;
        private Mock<ICallbackClient> _mockCallback;
        private Mock<IPayeeVerificationRequestParser> _mockParser;
        private Mock<INativeSigner> _mockSigner;
        private Mock<ISignatureService> _mockSignature;
        private Mock<ICallbackOrchestrator> _mockOrchestrator;
        private Mock<IJsonAdapter> _mockJsonAdapter;
        
        private IncomingVerificationHandler _handler;

        public VerificationLoopbackTests()
        {
            _mockPersistence = new Mock<IPersistenceGateway>();
            _mockInbound = new Mock<IInboundMessageService>();
            _mockIsoService = new Mock<IISOMessageService>();
            _mockCallback = new Mock<ICallbackClient>();
            _mockParser = new Mock<IPayeeVerificationRequestParser>();
            _mockSigner = new Mock<INativeSigner>();
            _mockSignature = new Mock<ISignatureService>();
            _mockOrchestrator = new Mock<ICallbackOrchestrator>();
            _mockJsonAdapter = new Mock<IJsonAdapter>();

            var mockLogger = new Mock<ILogger<IncomingVerificationHandler>>();
            var isoOptions = new ISO20022Options { Verification = "http://cb-url", Key = "key", Secret = "secret" };
            var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { CallbackInternalBudgetSeconds = 10 });

            // Fix CS0854
            _mockSigner.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns((string s, string a) => s);

            _handler = new IncomingVerificationHandler(
                isoOptions,
                mockLogger.Object,
                _mockSigner.Object,
                _mockJsonAdapter.Object,
                _mockPersistence.Object,
                _mockParser.Object,
                _mockSignature.Object,
                new Mock<ICorrelationService>().Object,
                _mockCallback.Object,
                _mockInbound.Object,
                _mockOrchestrator.Object,
                _mockIsoService.Object,
                coreOptions
            );
        }

        [Fact]
        public async Task HandleAsync_TakesOwnership_WhenLoopbackDetected()
        {
            // Arrange
            string msgId = "MSG_LOOPBACK";
            string xml = "<xml>dummy</xml>";
            var request = new PayeeVerificationBuilder.Request 
            { 
                MsgId = msgId, 
                SIPSRequestId = "TX_LOOPBACK",
                From = "BIC1", 
                To = "BIC1", // LOOPBACK: From == To
                BizMsgIdr = "BIZ1",
                MsgDefIdr = "acmt.023.001.03",
                CreDt = DateTime.UtcNow
            };

            // Setup Parse verification
            _mockInbound.Setup(x => x.VerifyAndParseAsync(It.IsAny<string>(), It.IsAny<Func<string, (bool, PayeeVerificationBuilder.Request?)>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync((true, request));

            // Setup Persistence to return Follower with Pending status
            var pendingMessage = new ISOMessage 
            { 
                Id = 1, 
                Status = TransactionStatus.Pending, 
                Response = null,
                FromBIC = "BIC1",
                ToBIC = "BIC1" 
            };

            _mockIsoService.Setup(s => s.TryRecordIncomingVerificationAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((pendingMessage, DedupOutcome.Follower, "MsgId"));

            // Setup CoreBank Callback success
            _mockOrchestrator.Setup(o => o.SendJsonAsync(It.IsAny<string>(), It.IsAny<System.Collections.Generic.Dictionary<string,string>>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<IJsonAdapter>(), It.IsAny<ICorrelationService>(), It.IsAny<JsonSerializerOptions>(), It.IsAny<ICallbackClient>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .ReturnsAsync(SIPS.ISO20022.Models.DTOs.Response<System.Text.Json.Nodes.JsonObject?>.Success(new System.Text.Json.Nodes.JsonObject()));

            _mockJsonAdapter.Setup(j => j.Transform(It.IsAny<System.Text.Json.Nodes.JsonObject>(), It.IsAny<string>()))
                .Returns((System.Text.Json.Nodes.JsonObject j, string s) => j);

            _mockJsonAdapter.Setup(j => j.ToObject<VerificationResponseDto>(It.IsAny<System.Text.Json.Nodes.JsonObject>()))
                .Returns(new VerificationResponseDto 
                { 
                    IsVerified = true,
                    Id = "ACCT123",
                    Name = "Test Name",
                    Address = "Test Address",
                    Currency = "USD"
                });

            // Act
            var result = await _handler.HandleAsync(xml, CancellationToken.None);

            // Assert
            // 1. Verify Loopback Event was appended (Owner claim via loopback)
            _mockIsoService.Verify(s => s.AppendAuditLedgerEventAsync(It.IsAny<int>(), It.Is<object>(o => o.ToString().Contains("VerificationLoopbackDetected")), It.IsAny<CancellationToken>()), Times.Once);

            // 2. Verify Response was persisted (Owner Path execution)
            _mockIsoService.Verify(s => s.PersistResponseAsync(It.IsAny<ISOMessage>(), TransactionStatus.Success, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
