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
        public static string CreateSamplePacs002(string txId, string status = "ACSC")
        {
            // Simplified XML that the fallback parser can handle
            return $"<Document><TxId>{txId}</TxId><Status>{status}</Status></Document>";
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
        protected Mock<IJsonAdapter> MockJsonAdapter { get; }
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
            MockJsonAdapter = new Mock<IJsonAdapter>();
            MockCallbackClient = new Mock<ICallbackClient>();
            MockSigner = new Mock<INativeSigner>();
            MockStatusOrchestrator = new Mock<IStatusOrchestrator>();
            MockCallbackOrchestrator = new Mock<ICallbackOrchestrator>();

            Options = new ISO20022Options
            {
                Transfer = "http://corebank/transfer",
                Return = "http://corebank/return",
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
        }

        protected IncomingPaymentStatusReportHandler CreateHandler()
        {
            var responseFactory = new ResponseFactory();
            var correlation = new CorrelationService();
            var parser = new PaymentStatusReportParser();
            var inbound = new InboundMessageService(MockSignatureService.Object);
            var isoService = new ISOMessageService(MockPersistence.Object);

            return new IncomingPaymentStatusReportHandler(
                Options,
                MockLogger.Object,
                MockSigner.Object,
                MockJsonAdapter.Object,
                parser,
                MockCallbackClient.Object,
                responseFactory,
                MockPersistence.Object,
                correlation,
                inbound,
                MockCallbackOrchestrator.Object,
                isoService,
                MockStatusOrchestrator.Object,
                CoreOptions
            );
        }

        protected void SetupCoreBankSuccessResponse(string txId)
        {
            MockJsonAdapter
                .Setup(x => x.Transform(It.IsAny<JsonObject>(), "CB_PaymentResponse"))
                .Returns(new JsonObject());

            MockJsonAdapter
                .Setup(x => x.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
                .Returns(TestHelpers.CreateCbsSuccessResponse(txId));

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
            MockJsonAdapter
                .Setup(x => x.Transform(It.IsAny<JsonObject>(), "CB_PaymentResponse"))
                .Returns(new JsonObject());

            MockJsonAdapter
                .Setup(x => x.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
                .Returns(TestHelpers.CreateCbsFailureResponse(txId));

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

            MockPersistence
                .Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            MockPersistence
                .Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ISOMessageStatus { ISOMessage = pendingTransaction });

            SetupCoreBankSuccessResponse(txId);

            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("ACSC", "ACSC", false))
                .Returns((TransactionStatus.Success, TransactionStatus.Success, "Payment completed successfully", "Credit applied"));

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().Contain(txId, "Response should contain the transaction ID");

            pendingTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should be updated to Success after CoreBank success");

            pendingTransaction.Reason.Should().Be("Payment completed successfully",
                "Transaction reason should reflect the successful completion");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Transfer,
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

            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("ACSC", "RJCT", false))
                .Returns((TransactionStatus.ReadyForReturn, TransactionStatus.ReadyForReturn,
                    "CoreBank rejected credit", "Account closed"));

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status should be ReadyForReturn when IPS accepts but CoreBank fails");

            pendingTransaction.Reason.Should().Be("CoreBank rejected credit",
                "Transaction reason should explain why it's ready for return");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Transfer,
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
                "CoreBank should still be called even if it fails");

            MockStatusOrchestrator.Verify(
                x => x.MapCompletionStatus("ACSC", "RJCT", false),
                Times.Once,
                "StatusOrchestrator should map ACSC + CBS failure to ReadyForReturn");
        }

        [Fact]
        public async Task HandleAsync_WhenRjctReceived_ShouldSetStatusToFailedAndNotCallCoreBank()
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

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus("RJCT"))
                .Returns(true);

            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("RJCT", null, false))
                .Returns((TransactionStatus.Failed, TransactionStatus.Failed,
                    "Payment rejected by IPS", "Insufficient funds"));

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.Failed,
                "Transaction status should be Failed when IPS rejects with RJCT");

            pendingTransaction.Reason.Should().Be("Payment rejected by IPS",
                "Transaction reason should explain the IPS rejection");

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
                "CoreBank should NOT be called for RJCT status");

            MockStatusOrchestrator.Verify(
                x => x.IsRejectionStatus("RJCT"),
                Times.Once,
                "StatusOrchestrator should be consulted to identify rejection status");

            MockStatusOrchestrator.Verify(
                x => x.MapCompletionStatus("RJCT", null, false),
                Times.Once,
                "StatusOrchestrator should map RJCT to Failed");
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
            MockJsonAdapter
                .Setup(x => x.Transform(It.IsAny<JsonObject>(), "CB_PaymentResponse"))
                .Returns(new JsonObject());

            MockJsonAdapter
                .Setup(x => x.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
                .Returns(TestHelpers.CreateCbsSuccessResponse(txId));

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    Options.Return,
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

            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("ACSC", "ACSC", false))
                .Returns((TransactionStatus.Success, TransactionStatus.Success, "Return completed", "Credit reversed"));

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            readyForReturnTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should be Success after successful return completion");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return,
                    It.IsAny<Dictionary<string, string>>(),
                    It.Is<CBReturnRequestDto>(dto =>
                        dto.OrgnlTxId == txId &&
                        dto.ReturnId == $"RTN-{txId}"),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank Return endpoint should be called for ReadyForReturn transaction");

            MockStatusOrchestrator.Verify(
                x => x.MapCompletionStatus("ACSC", "ACSC", false),
                Times.Once,
                "StatusOrchestrator should map return completion status");
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

            MockJsonAdapter
                .Setup(x => x.Transform(It.IsAny<JsonObject>(), "CB_PaymentResponse"))
                .Returns(new JsonObject());

            MockJsonAdapter
                .Setup(x => x.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
                .Returns(TestHelpers.CreateCbsSuccessResponse(txId));

            MockCallbackOrchestrator
                .Setup(x => x.SendJsonAsync(
                    Options.Return,
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

            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("ACSC", "ACSC", false))
                .Returns((TransactionStatus.Success, TransactionStatus.Success,
                    "Return completed successfully", "Credit reversed"));

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            readyForReturnTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should be Success when CoreBank successfully reverses credit");

            readyForReturnTransaction.Reason.Should().Be("Return completed successfully",
                "Transaction reason should reflect successful return completion");

            readyForReturnTransaction.AdditionalInfo.Should().Contain("reversed",
                "Additional info should indicate credit reversal");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank Return should be called exactly once");
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
                    Options.Return,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()))
                .ReturnsAsync((Response<JsonObject?>)null);

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

            readyForReturnTransaction.Reason.Should().Be("CoreBank return callback failed",
                "Transaction reason should explain the CoreBank failure");

            readyForReturnTransaction.AdditionalInfo.Should().Be("Manual intervention required to complete return",
                "Additional info should indicate manual intervention is needed");

            MockCallbackOrchestrator.Verify(
                x => x.SendJsonAsync(
                    Options.Return,
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CBReturnRequestDto>(),
                    "CB_ReturnRequest",
                    It.IsAny<IJsonAdapter>(),
                    It.IsAny<ICorrelationService>(),
                    It.IsAny<JsonSerializerOptions>(),
                    It.IsAny<ICallbackClient>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string>()),
                Times.Once,
                "CoreBank Return should be attempted even if it fails");

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
            result.Should().Contain(txId, "Response should contain the transaction ID");
            result.Should().Contain("ACSC", "Response should acknowledge with ACSC");

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
            result.Should().Contain(txId, "Response should contain the transaction ID");
            result.Should().Contain("ACSC", "Response should acknowledge with ACSC");

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
                .ReturnsAsync((ISOMessage)null);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response even for not found");
            result.Should().Contain("admi.002", "Response should be an administrative message for not found");

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
                .ReturnsAsync((Response<JsonObject?>)null);

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status should be ReadyForReturn when CoreBank returns null");

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

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status should be ReadyForReturn when CoreBank returns null data");

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

            MockPersistence.Verify(
                x => x.GetISOMessageWithTransactionsByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "Transaction lookup should not occur when signature is invalid");

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
            result.Should().Contain(txId, "Response should contain the transaction ID");
            result.Should().Contain("ACSC", "Response should acknowledge with ACSC for idempotent request");

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

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "Status should NOT be persisted again for idempotent request");
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

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");
            result.Should().Contain(txId, "Response should contain the transaction ID");
            result.Should().Contain("ACSC", "Response should acknowledge with ACSC for idempotent request");

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

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "Status should NOT be persisted again for idempotent request");
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

            // Critical: StatusOrchestrator determines final status
            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("ACSC", "ACSC", false))
                .Returns((TransactionStatus.Success, TransactionStatus.Success,
                    "Orchestrator mapped to Success", "Payment completed via orchestrator"));

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.Success,
                "Transaction status should be Success as determined by StatusOrchestrator");

            pendingTransaction.Reason.Should().Be("Orchestrator mapped to Success",
                "Reason should come from StatusOrchestrator");

            pendingTransaction.AdditionalInfo.Should().Contain("orchestrator",
                "Additional info should reflect orchestrator mapping");

            MockStatusOrchestrator.Verify(
                x => x.MapCompletionStatus("ACSC", "ACSC", false),
                Times.Once,
                "StatusOrchestrator should be consulted for status mapping");

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

            // Critical: StatusOrchestrator maps ACSC + CBS failure to ReadyForReturn
            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("ACSC", "RJCT", false))
                .Returns((TransactionStatus.ReadyForReturn, TransactionStatus.ReadyForReturn,
                    "Orchestrator mapped to ReadyForReturn", "CBS rejected, ready for return"));

            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus(It.IsAny<string>()))
                .Returns(false);

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "ACSC");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.ReadyForReturn,
                "Transaction status should be ReadyForReturn as determined by StatusOrchestrator");

            pendingTransaction.Reason.Should().Be("Orchestrator mapped to ReadyForReturn",
                "Reason should come from StatusOrchestrator");

            pendingTransaction.AdditionalInfo.Should().Contain("return",
                "Additional info should indicate ready for return");

            MockStatusOrchestrator.Verify(
                x => x.MapCompletionStatus("ACSC", "RJCT", false),
                Times.Once,
                "StatusOrchestrator should be consulted for status mapping");

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Final status should be persisted");
        }

        [Fact]
        public async Task HandleAsync_WhenStatusOrchestratorMapsRjctToFailed_ShouldPersistFailed()
        {
            // Arrange
            const string txId = "TX-ORCH-003";
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

            // Critical: StatusOrchestrator identifies RJCT as rejection
            MockStatusOrchestrator
                .Setup(x => x.IsRejectionStatus("RJCT"))
                .Returns(true);

            // Critical: StatusOrchestrator maps RJCT to Failed
            MockStatusOrchestrator
                .Setup(x => x.MapCompletionStatus("RJCT", null, false))
                .Returns((TransactionStatus.Failed, TransactionStatus.Failed,
                    "Orchestrator mapped RJCT to Failed", "Payment rejected by IPS"));

            var handler = CreateHandler();
            var pacs002Message = TestHelpers.CreateSamplePacs002(txId, "RJCT");

            // Act
            var result = await handler.HandleAsync(pacs002Message, CancellationToken.None);

            // Assert
            result.Should().NotBeNullOrEmpty("Handler should return a response");

            pendingTransaction.Status.Should().Be(TransactionStatus.Failed,
                "Transaction status should be Failed as determined by StatusOrchestrator");

            pendingTransaction.Reason.Should().Be("Orchestrator mapped RJCT to Failed",
                "Reason should come from StatusOrchestrator");

            pendingTransaction.AdditionalInfo.Should().Contain("rejected",
                "Additional info should indicate rejection");

            MockStatusOrchestrator.Verify(
                x => x.IsRejectionStatus("RJCT"),
                Times.Once,
                "StatusOrchestrator should be consulted to identify rejection");

            MockStatusOrchestrator.Verify(
                x => x.MapCompletionStatus("RJCT", null, false),
                Times.Once,
                "StatusOrchestrator should map RJCT to Failed");

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
                "CoreBank should NOT be called for RJCT status");

            MockPersistence.Verify(
                x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce,
                "Final status should be persisted");
        }
    }

    #endregion
}
