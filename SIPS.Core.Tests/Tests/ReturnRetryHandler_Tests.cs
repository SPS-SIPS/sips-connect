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
using SIPS.Core.Options;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Persistence;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class ReturnRetryHandler_Tests
{
    private static (ReturnRetryHandler sut, Mock<IIncomingRecorder> recorder, Mock<IInterfaceHttpClient> http, Mock<IJsonAdapter> adapter, Mock<IStatusOrchestrator> statusOrch)
        CreateSut(
            ISOMessage? isoMessage = null,
            Func<Response<JsonObject?>>? httpResultFactory = null,
            CBPaymentStatusResponseDto? cbResponse = null)
    {
        var options = new ISO20022Options
        {
            Return = "https://example.test/return",
            Key = "test-key",
            Secret = "test-secret"
        };

        var logger = Mock.Of<ILogger<ReturnRetryHandler>>();
        var cbLogger = Mock.Of<ILogger<CallbackClient>>();

        // Setup HTTP client mock
        var http = new Mock<IInterfaceHttpClient>();
        if (httpResultFactory != null)
        {
            http.Setup(h => h.Send(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StringContent>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => httpResultFactory());
        }

        // Setup JSON adapter mock
        var adapter = new Mock<IJsonAdapter>();
        adapter.Setup(a => a.Transform(It.IsAny<object>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });
        adapter.Setup(a => a.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
               .Returns(new JsonObject { ["mapped"] = true });

        var defaultCbResponse = cbResponse ?? new CBPaymentStatusResponseDto
        {
            Status = "ACSC",
            TxId = "TX123",
            EndToEndId = "E2E123",
            Reason = "Success",
            AdditionalInfo = "Completed"
        };

        adapter.Setup(a => a.ToObject<CBPaymentStatusResponseDto>(It.IsAny<JsonObject>()))
               .Returns(defaultCbResponse);

        // Setup recorder mock
        var recorder = new Mock<IIncomingRecorder>();
        var defaultIsoMessage = isoMessage ?? new ISOMessage
        {
            Id = 1,
            TxId = "TX123",
            EndToEndId = "E2E123",
            Status = TransactionStatus.ReadyForReturn,
            Round = 1,
            ReturnId = "RET123",
            FromBIC = "TESTBIC1",
            ToBIC = "TESTBIC2",
            Transactions = new List<Transaction>
            {
                new()
                {
                    TxId = "TX123",
                    EndToEndId = "E2E123",
                    Amount = 100m,
                    Currency = "USD",
                    DebtorName = "John Doe",
                    DebtorAccount = "ACC123",
                    CreditorName = "Jane Smith",
                    CreditorAccount = "ACC456"
                }
            }
        };

        recorder.Setup(r => r.GetISOMessageWithTransactionsByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(defaultIsoMessage);
        recorder.Setup(r => r.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage m, CancellationToken _) => m);

        // Setup status orchestrator mock
        var statusOrch = new Mock<IStatusOrchestrator>();
        statusOrch.Setup(s => s.MapCompletionStatus(It.IsAny<string>(), "ACSC", It.IsAny<bool>()))
                  .Returns((TransactionStatus.Success, TransactionStatus.Success, "Success", "Completed"));
        statusOrch.Setup(s => s.MapCompletionStatus(It.IsAny<string>(), "RJCT", It.IsAny<bool>()))
                  .Returns((TransactionStatus.ReadyForReturn, TransactionStatus.Failed, "Rejected", "Failed"));
        statusOrch.Setup(s => s.MapCompletionStatus(It.IsAny<string>(), "", It.IsAny<bool>()))
                  .Returns((TransactionStatus.ReadyForReturn, TransactionStatus.Failed, "No response", "Empty status"));
        statusOrch.Setup(s => s.IsSuccessStatus(It.IsAny<string>()))
                  .Returns((string s) => s == "ACSC");

        var correlation = new CorrelationService();
        var persistence = new PersistenceGateway(recorder.Object);
        var callback = new CallbackClient(http.Object, cbLogger, correlation, Microsoft.Extensions.Options.Options.Create(new CoreOptions()));
        var callbacks = new CallbackOrchestrator();
        var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var sut = new ReturnRetryHandler(
            options,
            logger,
            adapter.Object,
            callback,
            persistence,
            correlation,
            callbacks,
            statusOrch.Object,
            coreOptions);

        return (sut, recorder, http, adapter, statusOrch);
    }

    [Fact]
    public async Task RetryReturnAsync_Success_WhenCoreBankReturnsACSC()
    {
        // Arrange
        var (sut, recorder, _, _, _) = CreateSut(
            httpResultFactory: () => new Response<JsonObject?>(new JsonObject { ["status"] = "ACSC" })
            {
                StatusCode = HttpStatusCode.OK
            });

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().Be("CoreBank processing completed successfully");
        result.TxId.Should().Be("TX123");
        result.Status.Should().Be("Success");

        // Verify database was updated
        recorder.Verify(r => r.ISOMessageResponseAsync(
            It.Is<ISOMessage>(m => m.Status == TransactionStatus.Success && m.Round == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryReturnAsync_Fails_WhenCoreBankReturnsRJCT()
    {
        // Arrange
        var cbResponse = new CBPaymentStatusResponseDto
        {
            Status = "RJCT",
            TxId = "TX123",
            Reason = "Account not found",
            AdditionalInfo = "Creditor account invalid"
        };

        var (sut, recorder, _, _, _) = CreateSut(
            httpResultFactory: () => new Response<JsonObject?>(new JsonObject { ["status"] = "RJCT" })
            {
                StatusCode = HttpStatusCode.OK
            },
            cbResponse: cbResponse);

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("CoreBank retry failed");
        result.Status.Should().Be("ReadyForReturn");

        // Verify round was incremented
        recorder.Verify(r => r.ISOMessageResponseAsync(
            It.Is<ISOMessage>(m => m.Status == TransactionStatus.ReadyForReturn && m.Round == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryReturnAsync_ReturnsError_WhenTransactionNotFound()
    {
        // Arrange
        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.GetISOMessageWithTransactionsByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage?)null);

        var options = new ISO20022Options { Return = "https://example.test/return", Key = "key", Secret = "secret" };
        var logger = Mock.Of<ILogger<ReturnRetryHandler>>();
        var cbLogger = Mock.Of<ILogger<CallbackClient>>();
        var http = new Mock<IInterfaceHttpClient>();
        var adapter = new Mock<IJsonAdapter>();
        var correlation = new CorrelationService();
        var persistence = new PersistenceGateway(recorder.Object);
        var callback = new CallbackClient(http.Object, cbLogger, correlation, Microsoft.Extensions.Options.Options.Create(new CoreOptions()));
        var callbacks = new CallbackOrchestrator();
        var statusOrch = new Mock<IStatusOrchestrator>();
        var coreOptions = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var sut = new ReturnRetryHandler(options, logger, adapter.Object, callback, persistence, correlation, callbacks, statusOrch.Object, coreOptions);

        // Act
        var result = await sut.RetryReturnAsync("NONEXISTENT", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Transaction not found");
        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task RetryReturnAsync_ReturnsError_WhenTransactionNotInReadyForReturnStatus()
    {
        // Arrange
        var isoMessage = new ISOMessage
        {
            TxId = "TX123",
            Status = TransactionStatus.Pending, // Not ReadyForReturn
            Round = 1,
            Transactions = new List<Transaction> { new() { TxId = "TX123" } }
        };

        var (sut, _, _, _, _) = CreateSut(isoMessage: isoMessage);

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not in ReadyForReturn status");
        result.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task RetryReturnAsync_ReturnsError_WhenMaxRetriesExceeded()
    {
        // Arrange
        var isoMessage = new ISOMessage
        {
            TxId = "TX123",
            Status = TransactionStatus.ReadyForReturn,
            Round = 3, // Already at max retries
            ReturnId = "RET123",
            Transactions = new List<Transaction>
            {
                new() { TxId = "TX123", EndToEndId = "E2E123" }
            }
        };

        var (sut, recorder, http, _, _) = CreateSut(isoMessage: isoMessage);

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Maximum retry attempts (2) reached");
        result.Status.Should().Be("ReadyForReturn");
        result.AdditionalInfo.Should().Contain("Current round: 3");

        // Verify CoreBank was NOT called
        http.Verify(h => h.Send(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StringContent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryReturnAsync_IncrementsRound_OnFailedRetry()
    {
        // Arrange
        var isoMessage = new ISOMessage
        {
            TxId = "TX123",
            Status = TransactionStatus.ReadyForReturn,
            Round = 1,
            ReturnId = "RET123",
            Transactions = new List<Transaction>
            {
                new() { TxId = "TX123", EndToEndId = "E2E123" }
            }
        };

        var cbResponse = new CBPaymentStatusResponseDto
        {
            Status = "RJCT",
            TxId = "TX123"
        };

        var (sut, recorder, _, _, _) = CreateSut(
            isoMessage: isoMessage,
            httpResultFactory: () => new Response<JsonObject?>(new JsonObject { ["status"] = "RJCT" })
            {
                StatusCode = HttpStatusCode.OK
            },
            cbResponse: cbResponse);

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        recorder.Verify(r => r.ISOMessageResponseAsync(
            It.Is<ISOMessage>(m => m.Round == 2), // Round should be incremented
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryReturnAsync_DoesNotIncrementRound_OnSuccessfulRetry()
    {
        // Arrange
        var isoMessage = new ISOMessage
        {
            TxId = "TX123",
            Status = TransactionStatus.ReadyForReturn,
            Round = 2,
            ReturnId = "RET123",
            Transactions = new List<Transaction>
            {
                new() { TxId = "TX123", EndToEndId = "E2E123" }
            }
        };

        var (sut, recorder, _, _, _) = CreateSut(
            isoMessage: isoMessage,
            httpResultFactory: () => new Response<JsonObject?>(new JsonObject { ["status"] = "ACSC" })
            {
                StatusCode = HttpStatusCode.OK
            });

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        recorder.Verify(r => r.ISOMessageResponseAsync(
            It.Is<ISOMessage>(m => m.Round == 2), // Round should NOT be incremented
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryReturnAsync_HandlesNullResponse_FromCoreBank()
    {
        // Arrange - when HTTP client returns null, it will throw NullReferenceException
        // which is caught by the exception handler
        var (sut, _, _, _, _) = CreateSut(
            httpResultFactory: () => null!);

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Exception occurred");
    }

    [Fact]
    public async Task RetryReturnAsync_ReturnsError_WhenTransactionHasNoDetails()
    {
        // Arrange
        var isoMessage = new ISOMessage
        {
            TxId = "TX123",
            Status = TransactionStatus.ReadyForReturn,
            Round = 1,
            Transactions = new List<Transaction>() // Empty transactions
        };

        var (sut, _, _, _, _) = CreateSut(isoMessage: isoMessage);

        // Act
        var result = await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Transaction details not found");
        result.Status.Should().Be("NoTransactionDetails");
    }

    [Fact]
    public async Task RetryReturnAsync_CallsCoreBankWithCorrectUrl()
    {
        // Arrange
        var isoMessage = new ISOMessage
        {
            TxId = "TX123",
            Status = TransactionStatus.ReadyForReturn,
            Round = 1,
            ReturnId = "RET456",
            Transactions = new List<Transaction>
            {
                new() { TxId = "TX123", EndToEndId = "E2E123" }
            }
        };

        var (sut, _, http, _, _) = CreateSut(
            isoMessage: isoMessage,
            httpResultFactory: () => new Response<JsonObject?>(new JsonObject { ["status"] = "ACSC" })
            {
                StatusCode = HttpStatusCode.OK
            });

        // Act
        await sut.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert - Verify CoreBank was called
        http.Verify(h => h.Send(
            "https://example.test/return",
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StringContent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryReturnAsync_HandlesException_Gracefully()
    {
        // Arrange
        var recorder = new Mock<IIncomingRecorder>();
        recorder.Setup(r => r.GetISOMessageWithTransactionsByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Database error"));

        var (sut, _, _, _, _) = CreateSut();
        // We need to create a new SUT with the throwing recorder, but for simplicity,
        // we'll test the exception handling by having the HTTP client throw
        var (sutWithError, _, _, _, _) = CreateSut(
            httpResultFactory: () => throw new Exception("Network error"));

        // Act
        var result = await sutWithError.RetryReturnAsync("TX123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Exception occurred");
        result.Status.Should().Be("Error");
    }
}
