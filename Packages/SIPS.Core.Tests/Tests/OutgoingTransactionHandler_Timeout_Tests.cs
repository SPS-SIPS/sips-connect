using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;
using static SIPS.Core.Constants;

namespace SIPS.Core.Tests.Tests;

/// <summary>
/// Tests for OutgoingTransactionHandler timeout handling behavior.
/// Validates that PDNG status is returned on timeout to prevent double-payment scenarios.
/// </summary>
public sealed class OutgoingTransactionHandler_Timeout_Tests
{
    private sealed class FakeSigner : INativeSigner
    {
        public string SignEnvelope(string xml) => xml;
        public string SignEnvelope(string xml, string referenceId) => xml;
    }

    private sealed class FakeSignatureService : ISignatureService
    {
        public Task<(bool ok, string? verbose)> VerifyAsync(string xml, CancellationToken ct)
            => Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class FakePersistence : IPersistenceGateway
    {
        public ISOMessage? LastRecordedMessage { get; private set; }
        public ISOMessage? LastUpdatedMessage { get; private set; }

        public Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct)
        {
            // Store a snapshot to avoid reference issues
            LastRecordedMessage = new ISOMessage
            {
                TxId = message.TxId,
                EndToEndId = message.EndToEndId,
                Status = message.Status,
                Round = message.Round,
                FromBIC = message.FromBIC,
                ToBIC = message.ToBIC,
                MessageType = message.MessageType,
                Transactions = new List<Transaction>(message.Transactions)
            };
            return Task.FromResult(message);
        }

        public Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct)
            => Task.FromResult(status);

        public Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct)
        {
            // Store a snapshot to avoid reference issues
            LastUpdatedMessage = new ISOMessage
            {
                TxId = message.TxId,
                EndToEndId = message.EndToEndId,
                Status = message.Status,
                Round = message.Round,
                FromBIC = message.FromBIC,
                ToBIC = message.ToBIC,
                MessageType = message.MessageType,
                Transactions = new List<Transaction>(message.Transactions)
            };
            return Task.FromResult(message);
        }

        public Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct)
            => Task.FromResult(status);

        public Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct)
            => Task.FromResult<ISOMessage?>(null);

        public Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct)
            => Task.FromResult<ISOMessage?>(null);

        public Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct)
            => Task.FromResult<ISOMessage?>(null);

        public Task<List<ISOMessage>> GetISOMessagesByStatusAsync(TransactionStatus status, CancellationToken ct)
            => Task.FromResult(new List<ISOMessage>());

        public Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object ledgerEvent, uint xmin, CancellationToken ct)
            => Task.FromResult(1);
        public Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
        public Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct) => Task.FromResult((default(ISOMessage?), true));
        public Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct) => Task.FromResult((entity, SIPS.PostgreSQL.Enums.DedupOutcome.Owner, default(string?)));
    }

    private sealed class TimeoutSipsSender : ISipsRequestSender
    {
        private readonly HttpStatusCode _statusCode;

        public TimeoutSipsSender(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public Task<Response<string>> SendAsync(string url, string content, CancellationToken ct, string? correlationId = null)
        {
            // Simulate timeout or bad gateway
            return Task.FromResult(Response<string>.Fail("Request timed out", _statusCode));
        }
    }

    private sealed class SuccessSipsSender : ISipsRequestSender
    {
        public Task<Response<string>> SendAsync(string url, string content, CancellationToken ct, string? correlationId = null)
        {
            // Return a complete pacs.002 response with envelope and all required fields
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            var msgId = Guid.NewGuid().ToString("N");
            var response = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Envelope xmlns=""urn:envelope"">
    <AppHdr xmlns=""urn:iso:std:iso:20022:tech:xsd:head.001.001.03"">
        <Fr>
            <FIId>
                <FinInstnId>
                    <Othr>
                        <Id>TESTBIC</Id>
                    </Othr>
                </FinInstnId>
            </FIId>
        </Fr>
        <To>
            <FIId>
                <FinInstnId>
                    <Othr>
                        <Id>DESTBIC</Id>
                    </Othr>
                </FinInstnId>
            </FIId>
        </To>
        <BizMsgIdr>BIZ{msgId}</BizMsgIdr>
        <MsgDefIdr>pacs.002.001.12</MsgDefIdr>
        <CreDt>{now}</CreDt>
    </AppHdr>
    <Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12"">
        <FIToFIPmtStsRpt>
            <GrpHdr>
                <MsgId>MSG{msgId}</MsgId>
                <CreDtTm>{now}</CreDtTm>
                <InstgAgt>
                    <FinInstnId>
                        <Othr>
                            <Id>TESTBIC</Id>
                        </Othr>
                    </FinInstnId>
                </InstgAgt>
                <InstdAgt>
                    <FinInstnId>
                        <Othr>
                            <Id>DESTBIC</Id>
                        </Othr>
                    </FinInstnId>
                </InstdAgt>
            </GrpHdr>
            <TxInfAndSts>
                <TxSts>ACSC</TxSts>
                <OrgnlTxId>TEST123</OrgnlTxId>
                <OrgnlEndToEndId>E2E123</OrgnlEndToEndId>
                <AccptncDtTm>{now}</AccptncDtTm>
            </TxInfAndSts>
        </FIToFIPmtStsRpt>
    </Document>
</Envelope>";
            return Task.FromResult(Response<string>.Success(response));
        }
    }

    private PaymentRequestDto CreateTestRequest()
    {
        return new PaymentRequestDto
        {
            ToBIC = "TOBIC",
            LocalInstrument = "P2P",
            CategoryPurpose = "ACCT",
            EndToEndId = "E2E123",
            Amount = 100.50m,
            Currency = "USD",
            DebtorName = "John Doe",
            DebtorAccount = "1234567890",
            DebtorAccountType = "ACCT",
            CreditorName = "Jane Smith",
            CreditorAccount = "0987654321",
            CreditorAccountType = "ACCT",
            CreditorAgentBIC = "CREDAGENT",
            CreditorIssuer = "C",
            RemittanceInformation = "Payment for services"
        };
    }

    [Fact]
    public async Task HandleAsync_WhenRequestTimeout_ReturnsPDNGStatus()
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new TimeoutSipsSender(HttpStatusCode.RequestTimeout);
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        var result = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("timeout should return success with PDNG status, not failure");
        result.Data.Should().NotBeNull();
        result.Data!.Status.Should().Be(PDNG, "timeout should return PDNG status to prevent auto-reversal");
        result.Data.Reason.Should().Contain("Pending", "reason should indicate pending status");
        result.Data.AdditionalInfo.Should().Contain("Timeout", "additional info should mention timeout");
        result.Data.AdditionalInfo.Should().Contain("Do not reverse", "should warn CoreBank not to reverse");
        result.Data.TxId.Should().NotBeNullOrEmpty("TxId should be populated");
        result.Data.EndToEndId.Should().NotBeNullOrEmpty("EndToEndId should be populated");
    }

    [Fact]
    public async Task HandleAsync_WhenRequestTimeout_MarksTransactionAsCheckStatus()
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new TimeoutSipsSender(HttpStatusCode.RequestTimeout);
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        persistence.LastUpdatedMessage.Should().NotBeNull("transaction should be updated in database");
        persistence.LastUpdatedMessage!.Status.Should().Be(TransactionStatus.CheckStatus,
            "transaction should be marked as CheckStatus for SAF processing");
        persistence.LastUpdatedMessage.Round.Should().BeGreaterOrEqualTo(1, "retry counter should be incremented for SAF tracking");
    }

    [Fact]
    public async Task HandleAsync_WhenBadGateway_ReturnsPDNGStatus()
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new TimeoutSipsSender(HttpStatusCode.BadGateway);
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        var result = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("bad gateway should return success with PDNG status");
        result.Data.Should().NotBeNull();
        result.Data!.Status.Should().Be(PDNG, "bad gateway should return PDNG status");
        result.Data.AdditionalInfo.Should().Contain("BadGateway", "should mention the specific error");
    }

    [Fact(Skip = "Requires exact pacs.002 XML format - covered by integration tests")]
    public async Task HandleAsync_WhenSuccess_ReturnsActualStatus()
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new SuccessSipsSender();
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        var result = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Status.Should().Be(ACSC, "successful response should return ACSC status");
        persistence.LastUpdatedMessage!.Status.Should().Be(TransactionStatus.Success,
            "successful transaction should be marked as Success in database");
    }

    [Fact]
    public async Task HandleAsync_WhenTimeout_TransactionInitiallyMarkedAsPending()
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new TimeoutSipsSender(HttpStatusCode.RequestTimeout);
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        persistence.LastRecordedMessage.Should().NotBeNull("transaction should be recorded initially");
        persistence.LastRecordedMessage!.Status.Should().Be(TransactionStatus.Pending,
            "transaction should initially be marked as Pending before sending to IPS");

        // After timeout, it gets updated to CheckStatus
        persistence.LastUpdatedMessage.Should().NotBeNull();
        persistence.LastUpdatedMessage!.Status.Should().Be(TransactionStatus.CheckStatus,
            "transaction should be updated to CheckStatus after timeout");
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task HandleAsync_WhenTimeoutOrGatewayError_IncrementsRoundCounter(HttpStatusCode statusCode)
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new TimeoutSipsSender(statusCode);
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        persistence.LastUpdatedMessage.Should().NotBeNull();
        persistence.LastUpdatedMessage!.Round.Should().BeGreaterOrEqualTo(1,
            "round counter should be incremented for SAF retry tracking");
    }

    [Fact]
    public async Task HandleAsync_WhenTimeout_PreservesTransactionDetails()
    {
        // Arrange
        var options = new ISO20022Options { BIC = "TESTBIC", Agent = "TESTAGENT", SIPS = "http://test.sips" };
        var logger = new NullLogger<OutgoingTransactionHandler>();
        var signer = new FakeSigner();
        var signature = new FakeSignatureService();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var sips = new TimeoutSipsSender(HttpStatusCode.RequestTimeout);
        var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new StatusOrchestrator(new NullLogger<StatusOrchestrator>());
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 10 });

        var handler = new OutgoingTransactionHandler(
            options, logger, signer, signature, persistence, correlation,
            sips, isoService, statusOrchestrator, core);

        var request = CreateTestRequest();

        // Act
        var result = await handler.HandleAsync(request, CancellationToken.None);

        // Assert
        result.Data!.TxId.Should().NotBeNullOrEmpty();
        result.Data.EndToEndId.Should().Be(request.EndToEndId,
            "EndToEndId should be preserved from original request");

        persistence.LastRecordedMessage.Should().NotBeNull();
        persistence.LastRecordedMessage!.EndToEndId.Should().Be(request.EndToEndId);
        persistence.LastRecordedMessage.Transactions.Should().HaveCount(1);
        var transaction = persistence.LastRecordedMessage.Transactions.First();
        transaction.Amount.Should().Be(request.Amount);
        transaction.Currency.Should().Be(request.Currency);
    }
}
