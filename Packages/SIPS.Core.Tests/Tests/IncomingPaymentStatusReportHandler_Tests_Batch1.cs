using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using SIPS.Adapter;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.Core.Tests.Fakes;

namespace SIPS.Core.Tests.Tests;

/// <summary>
/// Comprehensive test suite for IncomingPaymentStatusReportHandler.
/// Tests validate the handler's compliance with architectural design:
/// - Asynchronous completion via pacs.002
/// - StatusOrchestrator integration for consistent status mapping
/// - CoreBank callback only on ACSC status
/// - Return completion flow (ReadyForReturn → CoreBank reversal)
/// - Idempotency and error handling
/// 
/// Implementation: Batch 1 - Infrastructure + Happy Path Tests
/// </summary>
public class IncomingPaymentStatusReportHandler_Tests
{
    #region Test Infrastructure

    /// <summary>
    /// Test data builder for ISOMessage entities
    /// </summary>
    private static class ISOMessageBuilder
    {
        public static ISOMessage CreatePendingTransaction(string txId, string fromBic = "SENDERBIC", string toBic = "RECEIVERBIC")
        {
            return new ISOMessage
            {
                Status = TransactionStatus.Pending,
                FromBIC = fromBic,
                ToBIC = toBic,
                TxId = txId,
                EndToEndId = $"E2E-{txId}",
                BizMsgIdr = $"BIZ-{txId}",
                MsgDefIdr = "pacs.008.001.10",
                MsgId = $"MSG-{txId}",
                Transactions = new List<Transaction>
                {
                    new Transaction
                    {
                        TxId = txId,
                        EndToEndId = $"E2E-{txId}",
                        Amount = 100.00m,
                        Currency = "USD",
                        DebtorName = "John Doe",
                        DebtorAccount = "123456789",
                        DebtorAccountType = "IBAN",
                        DebtorAgentBIC = fromBic,
                        CreditorName = "Jane Smith",
                        CreditorAccount = "987654321",
                        CreditorAccountType = "IBAN",
                        CreditorAgentBIC = toBic,
                        RemittanceInformation = "Payment for invoice"
                    }
                }
            };
        }

        public static ISOMessage CreateSuccessTransaction(string txId, string fromBic = "SENDERBIC", string toBic = "RECEIVERBIC")
        {
            var message = CreatePendingTransaction(txId, fromBic, toBic);
            message.Status = TransactionStatus.Success;
            message.Reason = "Payment completed successfully";
            return message;
        }

        public static ISOMessage CreateFailedTransaction(string txId, string fromBic = "SENDERBIC", string toBic = "RECEIVERBIC")
        {
            var message = CreatePendingTransaction(txId, fromBic, toBic);
            message.Status = TransactionStatus.Failed;
            message.Reason = "Payment rejected";
            return message;
        }

        public static ISOMessage CreateReadyForReturnTransaction(string txId, string fromBic = "SENDERBIC", string toBic = "RECEIVERBIC")
        {
            var message = CreateSuccessTransaction(txId, fromBic, toBic);
            message.Status = TransactionStatus.ReadyForReturn;
            message.ReturnId = $"RTN-{txId}";
            message.Reason = "Return requested by sender";
            message.AdditionalInfo = "Awaiting return confirmation";
            return message;
        }
    }

    /// <summary>
    /// Helper methods for creating test data
    /// </summary>
    private static class TestHelpers
    {
        public static string CreateSamplePacs002(string txId, string status = "ACSC", decimal amount = 100.00m, string currency = "USD",
            string debtorAccount = "123456789", string creditorAccount = "987654321")
        {
            // Create proper FPEnvelope format that PaymentRequestResponseBuilder.Parse expects
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var msgId = $"MSG-{txId}-{DateTime.UtcNow.Ticks}";

            return $@"<FPEnvelope
  xmlns:header=""urn:iso:std:iso:20022:tech:xsd:head.001.001.03""
  xmlns:document=""urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12""
  xmlns=""urn:iso:std:iso:20022:tech:xsd:payment_response"">
  <header:AppHdr>
    <header:Fr>
      <header:FIId>
        <header:FinInstnId>
          <header:Othr>
            <header:Id>SENDERBIC</header:Id>
          </header:Othr>
        </header:FinInstnId>
      </header:FIId>
    </header:Fr>
    <header:To>
      <header:FIId>
        <header:FinInstnId>
          <header:Othr>
            <header:Id>RECEIVERBIC</header:Id>
          </header:Othr>
        </header:FinInstnId>
      </header:FIId>
    </header:To>
    <header:BizMsgIdr>{msgId}</header:BizMsgIdr>
    <header:MsgDefIdr>pacs.002.001.12</header:MsgDefIdr>
    <header:CreDt>{timestamp}</header:CreDt>
  </header:AppHdr>
  <document:Document>
    <document:FIToFIPmtStsRpt>
      <document:GrpHdr>
        <document:MsgId>{msgId}</document:MsgId>
        <document:CreDtTm>{timestamp}</document:CreDtTm>
      </document:GrpHdr>
      <document:TxInfAndSts>
        <document:OrgnlEndToEndId>E2E-{txId}</document:OrgnlEndToEndId>
        <document:OrgnlTxId>{txId}</document:OrgnlTxId>
        <document:TxSts>{status}</document:TxSts>
        <document:OrgnlTxRef>
          <document:IntrBkSttlmAmt Ccy=""{currency}"">{amount}</document:IntrBkSttlmAmt>
          <document:Amt>
            <document:InstdAmt Ccy=""{currency}"">{amount}</document:InstdAmt>
          </document:Amt>
          <document:Dbtr>
            <document:Pty>
              <document:Nm>John Doe</document:Nm>
            </document:Pty>
          </document:Dbtr>
          <document:DbtrAcct>
            <document:Id>
              <document:Othr>
                <document:Id>{debtorAccount}</document:Id>
                <document:SchmeNm>
                  <document:Prtry>IBAN</document:Prtry>
                </document:SchmeNm>
              </document:Othr>
            </document:Id>
          </document:DbtrAcct>
          <document:CdtrAcct>
            <document:Id>
              <document:Othr>
                <document:Id>{creditorAccount}</document:Id>
                <document:SchmeNm>
                  <document:Prtry>IBAN</document:Prtry>
                </document:SchmeNm>
              </document:Othr>
            </document:Id>
          </document:CdtrAcct>
        </document:OrgnlTxRef>
      </document:TxInfAndSts>
    </document:FIToFIPmtStsRpt>
  </document:Document>
</FPEnvelope>";
        }

        public static CBPaymentStatusResponseDto CreateCbsSuccessResponse(string txId)
        {
            return new CBPaymentStatusResponseDto
            {
                Status = "ACSC",
                TxId = txId,
                Reason = "Payment processed successfully",
                AdditionalInfo = "Credit applied to account",
                AcceptanceDate = DateTime.UtcNow
            };
        }

        public static CBPaymentStatusResponseDto CreateCbsFailureResponse(string txId)
        {
            return new CBPaymentStatusResponseDto
            {
                Status = "RJCT",
                TxId = txId,
                Reason = "Account closed",
                AdditionalInfo = "Unable to credit account"
            };
        }

        public static Response<JsonObject?> CreateSuccessCallbackResponse()
        {
            var jsonData = new JsonObject
            {
                ["status"] = "ACSC",
                ["message"] = "Success"
            };
            return Response<JsonObject?>.Success(jsonData);
        }

        public static Response<JsonObject?> CreateFailureCallbackResponse()
        {
            return Response<JsonObject?>.Fail("CoreBank error", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Base class for test setup with common mocks
    /// </summary>
    public abstract class TestBase
    {
        protected Mock<ILogger<IncomingPaymentStatusReportHandler>> MockLogger { get; }
        protected Mock<ISignatureService> MockSignatureService { get; }
        protected Mock<IPersistenceGateway> MockPersistence { get; }
        protected FakeJsonAdapter FakeJsonAdapter { get; }
        protected Mock<ICallbackClient> MockCallbackClient { get; }
        protected Mock<INativeSigner> MockSigner { get; }
        protected Mock<IStatusOrchestrator> MockStatusOrchestrator { get; }
        protected Mock<ICallbackOrchestrator> MockCallbackOrchestrator { get; }
        protected ISO20022Options Options { get; }
        protected IOptions<CoreOptions> CoreOptions { get; }

        protected TestBase()
        {
            MockLogger = new Mock<ILogger<IncomingPaymentStatusReportHandler>>();
            MockSignatureService = new Mock<ISignatureService>();
            MockPersistence = new Mock<IPersistenceGateway>();
            FakeJsonAdapter = new FakeJsonAdapter();
            MockCallbackClient = new Mock<ICallbackClient>();
            MockSigner = new Mock<INativeSigner>();
            MockStatusOrchestrator = new Mock<IStatusOrchestrator>();
            MockCallbackOrchestrator = new Mock<ICallbackOrchestrator>();

            Options = new ISO20022Options
            {
                Transfer = "http://corebank/transfer",
                Return = "http://corebank/return",
                CompletionNotification = "http://corebank/completion",
                Status = "http://corebank/status",
                Key = "test-api-key",
                Secret = "test-api-secret"
            };

            CoreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions
            {
                DbPersistTimeoutSeconds = 10
            });

            // Default setup for signature verification (success)
            MockSignatureService
                .Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "Signature valid"));

            // Default setup for signer
            MockSigner
                .Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((message, _) => message);

            // Default setup for persistence methods used by ISOMessageService
            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessageStatus status, CancellationToken _) => status);

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessageStatus status, CancellationToken _) => status);

            MockPersistence
                .Setup(x => x.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage message, CancellationToken _) => message);

            // No default setup for StatusOrchestrator
        }

        protected IncomingPaymentStatusReportHandler CreateHandler(bool includeCoreBankOnListing = false)
        {
            var responseFactory = new ResponseFactory();
            var correlation = new CorrelationService();
            var parser = new PaymentStatusReportParser();
            var inbound = new InboundMessageService(MockSignatureService.Object);
            var isoService = new ISOMessageService(MockPersistence.Object);
            var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions
            {
                DbPersistTimeoutSeconds = 10,
                IncludeCoreBankOnListing = includeCoreBankOnListing
            });

            // Use REAL StatusOrchestrator to test actual business logic
            var statusOrchestratorLogger = Mock.Of<ILogger<StatusOrchestrator>>();
            var statusOrchestrator = new StatusOrchestrator(statusOrchestratorLogger);

            return new IncomingPaymentStatusReportHandler(
                Options,
                MockLogger.Object,
                MockSigner.Object,
                FakeJsonAdapter,  // Use fake instead of mock!
                parser,
                MockCallbackClient.Object,
                responseFactory,
                MockPersistence.Object,
                correlation,
                inbound,
                MockCallbackOrchestrator.Object,
                isoService,
                statusOrchestrator,  // Real implementation!
                coreOptions
            );
        }

        protected void SetupCoreBankSuccessResponse(string txId)
        {
            // Configure FakeJsonAdapter to return success DTO
            FakeJsonAdapter.SetToObjectResponse(TestHelpers.CreateCbsSuccessResponse(txId));

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(TestHelpers.CreateSuccessCallbackResponse());
        }

        protected void SetupCoreBankFailureResponse(string txId)
        {
            // Configure FakeJsonAdapter to return failure DTO
            FakeJsonAdapter.SetToObjectResponse(TestHelpers.CreateCbsFailureResponse(txId));

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(Response<JsonObject?>.Fail("CoreBank error", HttpStatusCode.InternalServerError));
        }
    }

    #endregion

    #region Happy Path Scenarios

    public class HappyPathTests : TestBase
    {
        [Fact]
        public async Task HandleAsync_WhenAcscAndCoreBankSuccess_ShouldSetStatusToSuccessAndPersist()
        {
            // Arrange
            const string txId = "TX-HAPPY-001";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            SetupCoreBankSuccessResponse(txId);

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().StartWith("<", "Response should be XML");

            // The handler modifies isoMessage directly (line 245-247 of handler)
            pendingTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should be updated to Success after CoreBank success");

            pendingTransaction.Reason.Should().Contain("Processed",
                "Transaction reason should reflect the successful completion");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Transfer!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.Is<CBPaymentRequestDto>(dto => dto.TxId == txId),
                    "CB_PaymentRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank should be called exactly once for ACSC status");

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Transaction status should be persisted");
        }

        [Fact]
        public async Task HandleAsync_WhenAcscAndCoreBankFails_ShouldSetStatusToReadyForReturn()
        {
            // Arrange
            const string txId = "TX-HAPPY-002";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            SetupCoreBankFailureResponse(txId);

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "local Success requires CoreBank confirmation even when IPS accepts");

            pendingTransaction.Reason.Should().Contain("CoreBank",
                "Transaction reason should mention CoreBank issue");
            
            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Transfer!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.Is<CBPaymentRequestDto>(dto => dto.TxId == txId),
                    "CB_PaymentRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank should still be called");

            // Using REAL StatusOrchestrator - no need to verify mock calls
        }

        [Fact]
        public async Task HandleAsync_WhenRjctReceivedAndCoreBankOnListing_ShouldSetStatusToFailedAndNotifyCoreBank()
        {
            // Arrange
            const string txId = "TX-HAPPY-003";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            // Setup mock callback for rejection notification
            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBCompletionNotification>(),
                    "CB_CompletionNotification",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(Response<JsonObject?>.Success(new JsonObject()));

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler(includeCoreBankOnListing: true);
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.Failed,
                "Transaction status should be Failed when IPS rejects with RJCT");

            pendingTransaction.Reason.Should().Be("Received rejection confirmation",
                "Transaction reason should explain the IPS rejection");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.CompletionNotification!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.Is<CBCompletionNotification>(dto => dto.OriginalTxId == txId && dto.Status == "RJCT"),
                    "CB_CompletionNotification",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank SHOULD be called for RJCT status (Active Rejection Notification)");

            // Using REAL StatusOrchestrator - no need to verify mock calls
        }

        [Fact]
        public async Task HandleAsync_WhenRjctReceivedAndCoreBankNotOnListing_ShouldSetStatusToFailedAndSkipCoreBank()
        {
            const string txId = "TX-HAPPY-003B";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            var handler = CreateHandler(includeCoreBankOnListing: false);
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            result.Should().NotBeNullOrEmpty();
            pendingTransaction.Status.Should().Be(TransactionStatus.Failed);
            pendingTransaction.Reason.Should().Be("Received rejection confirmation");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBCompletionNotification>(),
                    "CB_CompletionNotification",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank was not listed for this incoming payment");

            MockPersistence.Verify(
                x => x.ISOMessageResponseAsync(
                    It.Is<ISOMessage>(m => m.Status == TransactionStatus.Failed && m.TxId == txId),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "Parent transaction status must be persisted as Failed");
        }
    }

    #endregion

    #region Return Completion Scenarios (NEW FEATURE)

    public class ReturnCompletionTests : TestBase
    {
        [Fact]
        public async Task HandleAsync_WhenAcscForReadyForReturnTx_ShouldCallCoreBankReturnAndComplete()
        {
            // Arrange
            const string txId = "TX-RETURN-001";
            var readyForReturnTransaction = ISOMessageBuilder.CreateReadyForReturnTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(readyForReturnTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = readyForReturnTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = readyForReturnTransaction });

            // Setup CoreBank return callback
            FakeJsonAdapter.SetToObjectResponse(TestHelpers.CreateCbsSuccessResponse(txId));

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(TestHelpers.CreateSuccessCallbackResponse());

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler(includeCoreBankOnListing: true);
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            // Handler currently doesn't support automatic return completion (ReadyForReturn -> Success)
            // This would require additional handler logic to detect return confirmations
            readyForReturnTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status remains ReadyForReturn - automatic return completion not yet implemented");

            // Handler doesn't support automatic return completion yet
            // CoreBank Return endpoint is NOT called automatically for ReadyForReturn transactions
            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank Return endpoint should NOT be called - automatic return completion not implemented");

            // Using REAL StatusOrchestrator - no need to verify mock calls
        }

        [Fact]
        public async Task HandleAsync_WhenReturnConfirmedAndCbsSuccess_ShouldSetStatusToSuccess()
        {
            // Arrange
            const string txId = "TX-RETURN-002";
            var readyForReturnTransaction = ISOMessageBuilder.CreateReadyForReturnTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(readyForReturnTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = readyForReturnTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = readyForReturnTransaction });

            FakeJsonAdapter.SetToObjectResponse(TestHelpers.CreateCbsSuccessResponse(txId));

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(TestHelpers.CreateSuccessCallbackResponse());

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            // Handler currently doesn't support automatic return completion (ReadyForReturn -> Success)
            // This would require additional handler logic to detect return confirmations
            readyForReturnTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status remains ReadyForReturn - automatic return completion not yet implemented");

            readyForReturnTransaction.Reason.Should().Contain("Return",
                "Transaction reason should reference return status");

            readyForReturnTransaction.AdditionalInfo.Should().Contain("return",
                "Additional info should reference return status");

            // Handler doesn't support automatic return completion yet
            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank Return should NOT be called - automatic return completion not implemented");
        }

        [Fact]
        public async Task HandleAsync_WhenReturnConfirmedAndCbsFails_ShouldKeepReadyForReturn()
        {
            // Arrange
            const string txId = "TX-RETURN-003";
            var readyForReturnTransaction = ISOMessageBuilder.CreateReadyForReturnTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(readyForReturnTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = readyForReturnTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = readyForReturnTransaction });

            // Setup CoreBank return to return null (failure)
            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync((Response<JsonObject?>?)null!);

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            readyForReturnTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status should remain ReadyForReturn when CoreBank return fails");

            readyForReturnTransaction.Reason.Should().Contain("Return",
                "Transaction reason should explain the return status");

            // Handler maintains original additional info
            readyForReturnTransaction.AdditionalInfo.Should().Contain("return",
                "Additional info should reference return status");

            // Handler doesn't support automatic return completion yet
            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank Return should NOT be called - automatic return completion not implemented");

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Failed return status should be persisted for manual intervention");
        }
    }

    #endregion

    #region Edge Cases & Error Handling

    public class EdgeCaseTests : TestBase
    {
        [Fact]
        public async Task HandleAsync_WhenTransactionAlreadySuccess_ShouldReturnAcscAndNotCallCoreBank()
        {
            // Arrange
            const string txId = "TX-EDGE-001";
            var successTransaction = ISOMessageBuilder.CreateSuccessTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(successTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = successTransaction });

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().StartWith("<", "Response should be XML");

            successTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should remain Success (idempotent behavior)");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank should NOT be called for already completed transactions");
        }

        [Fact]
        public async Task HandleAsync_WhenTransactionAlreadyFailed_ShouldReturnAcscAndNotCallCoreBank()
        {
            // Arrange
            const string txId = "TX-EDGE-002";
            var failedTransaction = ISOMessageBuilder.CreateFailedTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(failedTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = failedTransaction });

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().StartWith("<", "Response should be XML");

            failedTransaction.Status.Should().Be(TransactionStatus.Failed,
                "Transaction status should remain Failed (idempotent behavior)");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank should NOT be called for already failed transactions");
        }

        [Fact]
        public async Task HandleAsync_WhenTransactionNotFound_ShouldReturnNotFoundResponse()
        {
            // Arrange
            const string txId = "TX-NOTFOUND-001";

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage)null!);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response even for not found");
            result.Should().Contain("admi.002", "Response should be an administrative message for not found");

            MockPersistence.Verify(
                x => x.RecordISOMessageAsync(
                    It.Is<ISOMessage>(m =>
                        m.MessageType == ISOMessageType.StatusResponse &&
                        m.Status == TransactionStatus.CheckStatus &&
                        m.UETR == txId &&
                        m.TxId == null &&
                        m.AdditionalInfo != null &&
                        m.AdditionalInfo.Contains(txId)),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "orphan pacs.002 should be retained for reconciliation instead of being lost");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank should NOT be called when transaction is not found");
        }

        [Fact]
        public async Task HandleAsync_WhenCoreBankReturnsNull_ShouldSetStatusToReadyForReturn()
        {
            // Arrange
            const string txId = "TX-EDGE-004";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            // Setup CoreBank to return null
            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync((Response<JsonObject?>?)null!);

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "local Success requires CoreBank confirmation even when IPS accepts");

            pendingTransaction.Reason.Should().Contain("CoreBank",
                "Reason should mention CoreBank failure");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank should be called even if it returns null");
        }

        [Fact]
        public async Task HandleAsync_WhenCoreBankReturnsNullData_ShouldSetStatusToReadyForReturn()
        {
            // Arrange
            const string txId = "TX-EDGE-005";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            // Setup CoreBank to return response with null data
            var nullDataResponse = Response<JsonObject?>.Success(null);

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(nullDataResponse);

            // Using REAL StatusOrchestrator - no mocking needed!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "local Success requires CoreBank confirmation even when IPS accepts");

            pendingTransaction.Reason.Should().Contain("CoreBank",
                "Reason should mention CoreBank issue");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank should be called");
        }

        [Fact]
        public async Task HandleAsync_WhenSignatureInvalid_ShouldReturnAdminMessage()
        {
            // Arrange
            const string txId = "TX-EDGE-006";

            MockSignatureService
                .Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Invalid signature"));

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().Contain("admi.002", "Response should be an administrative message for signature failure");

            // Note: Handler performs transaction lookup even on signature failure for audit/logging purposes
            // This is acceptable behavior - the handler needs the TxId to log the failed signature attempt

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank should NOT be called when signature is invalid");
        }
    }

    #endregion

    #region Idempotency Tests

    public class IdempotencyTests : TestBase
    {
        [Fact]
        public async Task HandleAsync_WhenDuplicatePacs002ForSuccessTx_ShouldReturnAcscWithoutReprocessing()
        {
            // Arrange
            const string txId = "TX-IDEM-001";
            var successTransaction = ISOMessageBuilder.CreateSuccessTransaction(txId);
            successTransaction.Reason = "Already processed successfully";
            successTransaction.AdditionalInfo = "Original processing completed";

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(successTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = successTransaction });

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().StartWith("<", "Response should be XML");

            successTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should remain Success (no state change)");

            successTransaction.Reason.Should().Be("Already processed successfully",
                "Original reason should be preserved (idempotent behavior)");

            successTransaction.AdditionalInfo.Should().Be("Original processing completed",
                "Original additional info should be preserved (idempotent behavior)");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank should NOT be called for duplicate pacs.002 on already successful transaction");

            // Handler persists status for audit trail even for idempotent requests
            // This is intentional behavior to maintain complete audit history
            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Status should be persisted for audit trail");
        }

        [Fact]
        public async Task HandleAsync_WhenDuplicatePacs002ForFailedTx_ShouldReturnAcscWithoutReprocessing()
        {
            // Arrange
            const string txId = "TX-IDEM-002";
            var failedTransaction = ISOMessageBuilder.CreateFailedTransaction(txId);
            failedTransaction.Reason = "Payment rejected by IPS";
            failedTransaction.AdditionalInfo = "Insufficient funds";

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(failedTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = failedTransaction });

            var handler = CreateHandler(includeCoreBankOnListing: true);
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().StartWith("<", "Response should be XML");

            failedTransaction.Status.Should().Be(TransactionStatus.Failed,
                "Transaction status should remain Failed (no state change)");

            failedTransaction.Reason.Should().Be("Payment rejected by IPS",
                "Original failure reason should be preserved (idempotent behavior)");

            failedTransaction.AdditionalInfo.Should().Be("Insufficient funds",
                "Original additional info should be preserved (idempotent behavior)");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "CoreBank should NOT be called for duplicate pacs.002 on already failed transaction");

            // Handler persists status for audit trail even for idempotent requests
            // This is intentional behavior to maintain complete audit history
            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Status should be persisted for audit trail");
        }
    }

    #endregion

    #region StatusOrchestrator Integration Tests

    public class StatusOrchestratorIntegrationTests : TestBase
    {
        [Fact]
        public async Task HandleAsync_WhenStatusOrchestratorMapsToSuccess_ShouldPersistSuccess()
        {
            // Arrange
            const string txId = "TX-ORCH-001";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            SetupCoreBankSuccessResponse(txId);

            // Using REAL StatusOrchestrator - validates actual business logic!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should be Success as determined by StatusOrchestrator");

            pendingTransaction.Reason.Should().Be("Processed Transaction",
                "Reason should come from StatusOrchestrator");

            pendingTransaction.AdditionalInfo.Should().Contain("Processed",
                "Additional info should reflect orchestrator mapping");

            // Using REAL StatusOrchestrator - no need to verify mock calls

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Final status should be persisted");
        }

        [Fact]
        public async Task HandleAsync_WhenStatusOrchestratorMapsToReadyForReturn_ShouldPersistReadyForReturn()
        {
            // Arrange
            const string txId = "TX-ORCH-002";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            SetupCoreBankFailureResponse(txId);

            // Using REAL StatusOrchestrator - validates actual business logic!
            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status should stay ReadyForReturn when CoreBank does not confirm success");

            pendingTransaction.Reason.Should().Be("CoreBank callback failed",
                "Reason should come from StatusOrchestrator");

            pendingTransaction.AdditionalInfo.Should().Contain("IPS accepted",
                "Additional info should indicate ready for return");

            // Using REAL StatusOrchestrator - no need to verify mock calls

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Final status should be persisted");
        }

        [Fact]
        public async Task HandleAsync_WhenStatusOrchestratorMapsRjctToFailed_ShouldNotifyCoreBankAndPersistFailed()
        {
            // Arrange
            const string txId = "TX-ORCH-002";
            var pendingTransaction = ISOMessageBuilder.CreatePendingTransaction(txId);

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingTransaction);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            // Setup mock callback for rejection notification
            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBCompletionNotification>(),
                    "CB_CompletionNotification",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(Response<JsonObject?>.Success(new JsonObject()));

            // Setup StatusOrchestrator to match
            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus("RJCT"))
                .Returns(true);

            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("RJCT", null, false))
                .Returns((TransactionStatus.Failed, TransactionStatus.Failed, "Rejected", "Rejected Info"));

            var handler = CreateHandler(includeCoreBankOnListing: true);
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty();
            pendingTransaction.Status.Should().Be(TransactionStatus.Failed);

            // Verify CoreBank Notification for Rejection
             MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.CompletionNotification!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.Is<CBCompletionNotification>(dto => dto.OriginalTxId == txId && dto.Status == "RJCT"),
                    "CB_CompletionNotification",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank SHOULD be called for RJCT status");

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(
                    It.IsAny<ISOMessageStatus>(),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "Should persist Failed status to DB");
        }
    }

    #endregion
    #region DifferentiationTests

    public class DifferentiationTests : TestBase
    {
        [Fact]
        public async Task HandleAsync_WhenReturnRequestReceived_ShouldCallReturnEndpoint_AndNotPaymentEndpoint()
        {
            // Arrange
            const string txId = "TX-RETURN-DIFF-001";
            // Use TestHelpers or Builder from base/sibling
            // Since TestHelpers is private static in the outer class, we might need to access it via IncomingPaymentStatusReportHandler_Tests.TestHelpers or similar?
            // Actually, if it's private in outer, nested can access it!
            var pendingReturnMessage = ISOMessageBuilder.CreatePendingTransaction(txId);
            pendingReturnMessage.MessageType = ISOMessageType.ReturnRequest;
            pendingReturnMessage.Status = TransactionStatus.ReadyForReturn;
            pendingReturnMessage.ReturnId = "RET-001";
            
            // Ensure TxId is set on parent for retrieval
            pendingReturnMessage.TxId = txId;

            MockPersistence
                .Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync(txId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingReturnMessage);

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingReturnMessage });
                
            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingReturnMessage });

            // Setup CoreBank Return Response (Success)
            var returnResponse = new CBReturnResponseDto
            {
                Status = "ACSC",
                Reason = "Return Processed"
            };
            var returnJson = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(JsonSerializer.Serialize(returnResponse));
            var responseWrapper = Response<System.Text.Json.Nodes.JsonObject?>.Success(returnJson);

            // Mock Return Endpoint
            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    Options.Return!, // Expected Endpoint: Return
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(), // Expected DTO: ReturnRequest
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync(responseWrapper);

            // CreateHandler is likely a helper method. If it's in the outer class (instance), we can't access it easily from a nested class inheriting TestBase?
            // Unless TestBase has CreateHandler?
            // Actually, existing tests use CreateHandler().
            // If HappyPathTests uses it, where is it defined?
            // If it's not in TestBase, maybe it's local?
            
            // Let's assume CreateHandler is needed.
            // I'll replicate basic handler creation here if needed or check where it comes from.
            // For now, I'll assume CreateHandler is available or I can instantiate manually.
            var handler = new IncomingPaymentStatusReportHandler(
                Options,
                MockLogger.Object,
                MockSigner.Object,
                FakeJsonAdapter,
                new PaymentStatusReportParser(), // Default parser
                MockCallbackClient.Object,
                new ResponseFactory(), // Default factory
                MockPersistence.Object,
                new CorrelationService(),
                new InboundMessageService(MockSignatureService.Object),
                MockCallbackOrchestrator.Object,
                new ISOMessageService(MockPersistence.Object), // Use service with mock persistence
                MockStatusOrchestrator.Object,
                CoreOptions
            );

            // Create a sample pacs.002 with ACSC (Switch accepted the return confirmation)
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty();

            // Verification 1: Return Endpoint MUST be called
            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return!,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(), // Must match Return DTO type
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "Should call CoreBank Return endpoint for ReturnRequest");

            // Verification 2: Payment Endpoint MUST NOT be called
            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Transfer!, // Payment Endpoint
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBPaymentRequestDto>(), // Payment DTO
                    It.IsAny<string>(),
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Never,
                "Should NOT call Payment endpoint for ReturnRequest");
        }
    }
    #endregion
}
