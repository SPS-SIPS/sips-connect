
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
// using SIPS.Core.Constants; removed
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
using static SIPS.Core.Constants; 
using System.Net.Http; 

namespace SIPS.Core.Tests.DataWipingSafety 
{
    public class DataWipingSafetyTests
    {
        private Mock<IPersistenceGateway> _mockPersistence;
        private Mock<SIPS.Core.Services.Abstractions.IISOMessageService> _mockIsoService;
        private Mock<IInterfaceHttpClient> _mockHttpClient;
        private OutgoingReturnTransactionHandler _handler;
        private ISOMessageService _realIsoService; 

        public DataWipingSafetyTests()
        {
            _mockPersistence = new Mock<IPersistenceGateway>();
            _mockHttpClient = new Mock<IInterfaceHttpClient>();
            _mockIsoService = new Mock<SIPS.Core.Services.Abstractions.IISOMessageService>();

            var mockLogger = new Mock<ILogger<OutgoingReturnTransactionHandler>>();
            var mockSigner = new Mock<INativeSigner>();
            var mockVerifier = new Mock<INativeVerifier>();
            var mockRecorder = new Mock<IIncomingRecorder>();
            var mockSignatureService = new Mock<ISignatureService>();
            var mockCorrelation = new Mock<ICorrelationService>();
            var mockStatusOrchestrator = new Mock<IStatusOrchestrator>();
            
             mockSignatureService.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                                .ReturnsAsync((true, null));

            // Fix CS0854 - expression tree cannot contain optional argument usage.
            // string SignEnvelope(string message, string algorithm = "SHA1withRSA");
            mockSigner.Setup(s => s.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns((string s, string a) => s);

            var isoOptions = new ISO20022Options { SIPS = "http://test-url", BIC = "TESTBIC" };
            // Fix Options ambiguity
            var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 1 });

            _realIsoService = new ISOMessageService(_mockPersistence.Object, NullLogger<ISOMessageService>.Instance);
            
             // Removed mockStatusOrchestrator setup as it's not needed for failure paths and caused expression tree issues
             // Do NOT put it back.

            _handler = new OutgoingReturnTransactionHandler(
                isoOptions,
                mockLogger.Object,
                _mockHttpClient.Object,
                mockSigner.Object,
                mockVerifier.Object,
                mockRecorder.Object,
                mockSignatureService.Object,
                _mockPersistence.Object,
                mockCorrelation.Object,
                _realIsoService, 
                mockStatusOrchestrator.Object,
                coreOptions
            );
        }

        [Fact]
        public async Task MarkForCheckStatusAsync_PreservesAuditLedger_WhenResponseIsNull()
        {
            // Arrange
            var txId = "TX123";
            
            var msg = new ISOMessage 
            { 
                Id = 1, 
                TxId = txId, 
                CoreBankResponse = null 
            };

            // EXPECTATION: The persistence layer receives the update.
            _mockPersistence.Setup(p => p.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .Callback<ISOMessage, CancellationToken>((m, c) => 
                {
                    Assert.Equal(TransactionStatus.CheckStatus, m.Status);
                    Assert.Null(m.CoreBankResponse); 
                })
                .ReturnsAsync(msg); 

            // Act
            await _realIsoService.MarkForCheckStatusAsync(msg, "Timeout", CancellationToken.None);

            // Assert
            _mockPersistence.Verify(p => p.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NetworkTimeout_Triggers_Safely_MarkForCheckStatus()
        {
            // Arrange
            var req = new ReturnPaymentRequestDto { OriginalTxId = "TX_ORIG", ReturnId = "RET_1", Reason = "RSN", AdditionalInfo = "INF" };
            
            var originalMsg = new ISOMessage 
            { 
                Id = 100,
                TxId = "TX_ORIG",
                Status = TransactionStatus.Success,
                CoreBankResponse = null,
                 FromBIC = "TESTBIC",
                 ToBIC = "OTHERBIC",
                 Date = DateTime.UtcNow
            };
            originalMsg.Transactions.Add(new Transaction { 
                TxId = "TX_ORIG", 
                EndToEndId = "E2E", 
                LocalInstrument = "INST", 
                Amount = 100, 
                Currency = "USD",
                FromBIC = "OTHERBIC",
                CreditorAgentBIC = "TESTBIC" // REQUIRED: Must match _configuration.BIC ("TESTBIC") to pass validation check
            });

            _mockPersistence.Setup(p => p.GetISOMessageWithTransactionsByTxIdAsync("TX_ORIG", It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalMsg);
            
            _mockPersistence.Setup(p => p.GetISOMessagesByOriginalTxIdAndTypeAsync(It.IsAny<string>(), It.IsAny<ISOMessageType>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Collections.Generic.List<ISOMessage>());

            _mockPersistence.Setup(p => p.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessage { Id = 200, Status = TransactionStatus.Pending });


            // SIMULATE TIMEOUT 
            _mockHttpClient.Setup(h => h.Send4XML(It.IsAny<string>(), It.IsAny<StringContent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response<string>.Fail("Timeout", System.Net.HttpStatusCode.RequestTimeout));

             // Capture the message from ISOMessageResponseAsync
            ISOMessage? capturedMessage = null;
            _mockPersistence.Setup(p => p.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                 .Callback<ISOMessage, CancellationToken>((m, c) => capturedMessage = m)
                 .ReturnsAsync((ISOMessage m, CancellationToken c) => m);

            // Act
            await _handler.HandleAsync(req, CancellationToken.None);

            // Assert
            Assert.NotNull(capturedMessage);
            Assert.Equal(TransactionStatus.CheckStatus, capturedMessage.Status);
            // This verifies that even though coreBankResponse was null, we went through safe path
            Assert.Null(capturedMessage.CoreBankResponse);
        }
    }
}
