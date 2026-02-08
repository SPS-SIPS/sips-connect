using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.Core.Services.Persistence;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.Core.Services.Verification;
using Xunit;

namespace SIPS.Core.Tests.Tests
{
    public sealed class OutgoingTransactionHandler_Token_Tests
    {
        private sealed class FakeSigner : INativeSigner
        {
            public string SignEnvelope(string xml) => xml;
            public string SignEnvelope(string xml, string referenceId) => xml;
        }
        private sealed class FakeSignatureService : ISignatureService
        {
            public Task<(bool ok, string? verbose)> VerifyAsync(string xml, CancellationToken ct) => Task.FromResult<(bool, string?)>((true, null));
        }
        private sealed class FakePersistence : IPersistenceGateway
        {
            public CancellationToken? LastToken { get; private set; }
            public Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct)
            {
                LastToken = ct;
                return Task.FromResult(message);
            }
            public Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct) => Task.FromResult(status);
            public Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct) { LastToken = ct; return Task.FromResult(message); }
            public Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct) { LastToken = ct; return Task.FromResult(status); }
            public Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
            public Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
            public Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
            public Task<System.Collections.Generic.List<ISOMessage>> GetISOMessagesByStatusAsync(SIPS.PostgreSQL.Enums.TransactionStatus status, CancellationToken ct) => Task.FromResult(new System.Collections.Generic.List<ISOMessage>());
            public Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object entry, uint expectedXmin, CancellationToken ct) => Task.FromResult(1);
            public Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
            public Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct) => Task.FromResult((default(ISOMessage?), true));
            public Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct) => Task.FromResult((entity, SIPS.PostgreSQL.Enums.DedupOutcome.Owner, default(string?)));
        }
        private sealed class FakeSipsSender : ISipsRequestSender
        {
            public Task<Response<string>> SendAsync(string url, string content, CancellationToken ct, string? correlationId = null)
            {
                // Return a minimal response to proceed through handler
                return Task.FromResult(Response<string>.Success("<ok/>"));
            }
        }

        [Fact]
        public async Task OutgoingTransaction_Persists_With_DbToken_Not_RequestToken()
        {
            var options = new ISO20022Options { BIC = "BICX", Agent = "AGTX", SIPS = "http://unit.test/sips" };
            var logger = new NullLogger<OutgoingTransactionHandler>();
            var signer = new FakeSigner();
            var sig = new FakeSignatureService();
            var persistence = new FakePersistence();
            var correlation = new CorrelationService();
            var sips = new FakeSipsSender();
            var isoService = new ISOMessageService(persistence);
            var statusOrchestrator = new Mock<IStatusOrchestrator>().Object;
            var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 1 });

            var handler = new OutgoingTransactionHandler(options, logger, signer, sig, persistence, correlation, sips, isoService, statusOrchestrator, core);

            var req = new PaymentRequestDto
            {
                ToBIC = "TOBIC",
                LocalInstrument = "P2P",
                CategoryPurpose = "ACCT",
                EndToEndId = "E2E",
                Amount = 1,
                Currency = "USD",
                DebtorName = "D",
                DebtorAccount = "DA",
                DebtorAccountType = "ACCT",
                CreditorName = "C",
                CreditorAccount = "CA",
                CreditorAccountType = "ACCT",
                CreditorAgentBIC = "CABIC",
                CreditorIssuer = "C"
            };

            using var userCts = new CancellationTokenSource();
            userCts.Cancel(); // simulate a canceled incoming request token

            var _ = await handler.HandleAsync(req, userCts.Token);

            Assert.NotNull(persistence.LastToken);
            Assert.False(persistence.LastToken!.Value.IsCancellationRequested);
            Assert.NotEqual(userCts.Token, persistence.LastToken!.Value);
        }
    }
}
