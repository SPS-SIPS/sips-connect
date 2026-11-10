using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using System.Collections.Concurrent;
using System.Net;
using System.Xml.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using SIPS.Core.Options;
using SIPS.Core.Services.Implementations;
using SIPS.ISO20022.Helpers;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;

namespace SIPS.Core.Tests.Tests;

/// <summary>
/// Proposed domestic outgoing contract. These tests intentionally run against the
/// production handler, not a replacement implementation. The shared store models
/// the existing PostgreSQL unique constraint; it does not prove database isolation.
/// </summary>
public sealed class OutgoingTransactionIdempotencyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_localId_is_bad_request_without_signing_persistence_or_network(string? localId)
    {
        var h = new Harness();
        var result = await h.Handler().HandleAsync(Request(localId), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("localId", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Rows);
        Assert.Equal(0, h.SignCount);
        Assert.Equal(0, h.SendCount);
    }

    [Theory]
    [InlineData(TransactionStatus.Success, "ACSC")]
    [InlineData(TransactionStatus.Failed, "PDNG")]
    [InlineData(TransactionStatus.Pending, "PDNG")]
    [InlineData(TransactionStatus.CheckStatus, "PDNG")]
    public async Task Existing_localId_returns_original_state_without_resubmission(TransactionStatus state, string expected)
    {
        var h = new Harness();
        var original = h.Seed(state);
        var result = await h.Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(expected, result.Data.Status);
        Assert.Equal(original.TxId, result.Data.TxId);
        Assert.Equal(original.EndToEndId, result.Data.EndToEndId);
        Assert.Equal(0, h.SendCount);
        Assert.Equal(0, h.SignCount);
        Assert.Same(original, Assert.Single(h.Rows).Value);
    }

    [Theory]
    [InlineData(null, "BANK-123", "LOCAL-123")]
    [InlineData("malformed", "BANK-123", "LOCAL-123")]
    [InlineData("ACSP", "BANK-123", "LOCAL-123")]
    [InlineData("ACSC", "BANK-123", "LOCAL-123")]
    [InlineData("", "BANK-123", "LOCAL-123")]
    [InlineData("RJCT", "OTHER-TX", "LOCAL-123")]
    [InlineData("RJCT", "BANK-123", "OTHER-LOCAL")]
    public async Task Recovery_exhaustion_without_matching_rejection_remains_pending(string? status, string txId, string localId)
    {
        var h = new Harness();
        var row = h.Seed(TransactionStatus.CheckStatus);
        row.Response = status is null ? null : Encoding.UTF8.GetBytes(
            status == "malformed" ? "not XML" : NetworkReport(status, txId, localId));
        await h.ExhaustRecovery(row);
        Assert.Equal(TransactionStatus.Failed, row.Status); // Real recovery state transition.
        var replay = await h.Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.True(replay.IsSuccess);
        Assert.Equal("PDNG", replay.Data?.Status);
        Assert.Equal("OUTCOME_UNRESOLVED", replay.Data?.Reason);
        Assert.Contains("reconcil", replay.Data?.AdditionalInfo ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, h.SendCount);
    }

    [Fact]
    public async Task Matching_network_rejection_is_replayed_as_rejected_even_if_recovery_overwrote_internal_reason()
    {
        var h = new Harness();
        var row = h.Seed(TransactionStatus.CheckStatus);
        var xml = NetworkReport("RJCT", "BANK-123", "LOCAL-123");
        Assert.Equal("RJCT", PaymentRequestResponseBuilder.Parse(xml).Status);
        row.Response = Encoding.UTF8.GetBytes(xml);
        await h.ExhaustRecovery(row);
        var replay = await h.Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("RJCT", replay.Data?.Status);
        Assert.Equal("AC04", replay.Data?.Reason);
        Assert.Equal(0, h.SendCount);
    }

    internal static string NetworkReport(string status, string txId, string localId)
    {
        var xml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = "NETWORK", To = "BICX", Status = status.Length == 0 ? "ACSP" : status,
            Reason = "AC04", Original = new PaymentRequestBuilder.Request
            {
                From = "BICX", To = "TOBIC", BizMsgIdr = "ORIGINAL-BIZ", MsgId = "ORIGINAL-MSG",
                MsgDefIdr = "pacs.008.001.10", CreDt = DateTime.UtcNow,
                TxId = txId, EndToEndId = localId, Amount = 100, Currency = "USD"
            }
        });
        if (status.Length != 0) return xml;
        var document = XDocument.Parse(xml);
        document.Descendants().Where(x => x.Name.LocalName == "TxSts").Remove();
        return document.ToString();
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("beneficiary")]
    [InlineData("txId")]
    public async Task Reused_localId_with_changed_payload_returns_original_and_explicit_replay_notice(string changed)
    {
        var h = new Harness();
        var original = h.Seed(TransactionStatus.Success);
        var bytes = original.Message.ToArray();
        var request = Request();
        if (changed == "amount") request.Amount = 999;
        if (changed == "beneficiary") request.CreditorAccount = "OTHER-ACCOUNT";
        if (changed == "txId") request.TxId = "NEW-TX-ID";
        var result = await h.Handler().HandleAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess); // Requested policy: return original, not DUPLICATE_CONFLICT.
        Assert.Equal("ACSC", result.Data?.Status);
        Assert.Equal(original.EndToEndId, result.Data?.EndToEndId);
        Assert.Contains("original", result.Data?.AdditionalInfo ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes, original.Message);
        Assert.Equal(100m, Assert.Single(original.Transactions).Amount);
        Assert.Equal("CA", Assert.Single(original.Transactions).CreditorAccount);
        Assert.Equal(0, h.SendCount);
        Assert.Equal(0, h.SignCount);
    }

    [Fact]
    public async Task Lost_response_retry_through_fresh_handler_returns_pending_without_second_network_send()
    {
        var h = new Harness();
        // The network received the submission, but the sender cannot obtain its result.
        var first = await h.Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("PDNG", first.Data?.Status);
        Assert.Equal(1, h.SendCount);
        // The bank loses even this response and repeats the original instruction.
        var retry = await h.Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.True(retry.IsSuccess);
        Assert.Equal("PDNG", retry.Data?.Status);
        Assert.Equal(first.Data?.TxId, retry.Data?.TxId);
        Assert.Equal("LOCAL-123", retry.Data?.EndToEndId);
        Assert.Single(h.Rows);
        Assert.Equal(1, h.SendCount);
    }

    [Fact]
    public async Task Concurrent_retries_on_two_handlers_have_one_submission_and_a_pending_replay()
    {
        var h = new Harness { HoldNetwork = true };
        var first = h.Handler().HandleAsync(Request(), CancellationToken.None);
        try
        {
            await h.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = await h.Handler().HandleAsync(Request(), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(second.IsSuccess);
            Assert.Equal("PDNG", second.Data?.Status);
            Assert.Single(h.Rows);
            Assert.Equal(1, h.SendCount);
        }
        finally
        {
            h.Release.TrySetResult(true);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Simultaneous_first_inserts_resolve_unique_constraint_race_as_pending_replay()
    {
        var h = new Harness { RaceInserts = true };
        var results = await Task.WhenAll(
            h.Handler().HandleAsync(Request(), CancellationToken.None),
            h.Handler().HandleAsync(Request(), CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess);
            Assert.Equal("PDNG", result.Data?.Status);
            Assert.False(string.IsNullOrWhiteSpace(result.Data?.TxId));
        });
        Assert.Single(h.Rows);
        Assert.Equal(1, h.SendCount);
    }

    [Fact]
    public async Task Different_localIds_with_same_payment_details_are_distinct_instructions()
    {
        var h = new Harness();
        var first = await h.Handler().HandleAsync(Request("BANK-1"), CancellationToken.None);
        var second = await h.Handler().HandleAsync(Request("BANK-2"), CancellationToken.None);
        Assert.Equal("PDNG", first.Data?.Status);
        Assert.Equal("PDNG", second.Data?.Status);
        Assert.Equal(2, h.Rows.Count);
        Assert.Equal(2, h.SendCount);
    }

    [Fact]
    public async Task Storage_failure_prevents_network_submission()
    {
        var h = new Harness { FailStorage = true };
        var result = await h.Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Empty(h.Rows);
        Assert.Equal(0, h.SendCount);
    }

    private static PaymentRequestDto Request(string? localId = "LOCAL-123") => new()
    {
        TxId = null!, EndToEndId = localId!, ToBIC = "TOBIC", LocalInstrument = "P2P",
        CategoryPurpose = "ACCT", Amount = 100, Currency = "USD",
        DebtorName = "D", DebtorAccount = "DA", DebtorAccountType = "ACCT",
        CreditorName = "C", CreditorAccount = "CA", CreditorAccountType = "ACCT",
        CreditorAgentBIC = "CABIC", CreditorIssuer = "C"
    };

    private sealed class Harness
    {
        public readonly ConcurrentDictionary<string, ISOMessage> Rows = new();
        public readonly TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldNetwork, FailStorage, RaceInserts;
        private int insertArrivals;
        private readonly TaskCompletionSource<bool> bothInserts = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SendCount, SignCount;
        private readonly Mock<IPersistenceGateway> persistence = new();
        private readonly Mock<INativeSigner> signer = new();
        private readonly Mock<ISipsRequestSender> sender = new();
        private readonly Mock<IISOMessageService> iso = new();

        public Harness()
        {
            persistence.Setup(x => x.GetISOMessageByTxIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) => Rows.GetValueOrDefault(id));
            persistence.Setup(x => x.GetDomesticOutgoingByLocalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, string _, CancellationToken __) => Rows.GetValueOrDefault(id));
            persistence.Setup(x => x.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISOMessage row, CancellationToken _) =>
                {
                    if (RaceInserts)
                    {
                        if (Interlocked.Increment(ref insertArrivals) == 2) bothInserts.TrySetResult(true);
                        await bothInserts.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    if (FailStorage) throw new IOException("Database unavailable");
                    if (!Rows.TryAdd(row.DomesticRequestId!, row))
                        throw new DbUpdateException("Duplicate payment", new PostgresException(
                            "Duplicate payment", "ERROR", "ERROR", "23505", constraintName: "ux_domestic_outgoing_local_id"));
                    return row;
                });
            signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns((string xml, string algorithm) =>
            {
                Interlocked.Increment(ref SignCount);
                return xml;
            });
            sender.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .Returns(async (string _, string __, CancellationToken ___, string? ____) =>
                {
                    Interlocked.Increment(ref SendCount);
                    Entered.TrySetResult(true);
                    if (HoldNetwork) await Release.Task;
                    return Response<string>.Fail("Network response lost", HttpStatusCode.RequestTimeout);
                });
            iso.Setup(x => x.MarkForCheckStatusAsync(It.IsAny<ISOMessage>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .Callback((ISOMessage row, string _, CancellationToken __, bool ___) => row.Status = TransactionStatus.CheckStatus)
                .Returns(Task.CompletedTask);
        }

        public Task ExhaustRecovery(ISOMessage row)
        {
            persistence.Setup(x => x.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage message, CancellationToken _) => message);
            return new ISOMessageService(persistence.Object).FinalizeAfterMaxRetriesAsync(
                row, "No authoritative response after retries", CancellationToken.None);
        }

        public ISOMessage Seed(TransactionStatus status)
        {
            var row = new ISOMessage
            {
                TxId = "BANK-123", EndToEndId = "LOCAL-123", MessageType = ISOMessageType.TransactionRequest,
                Status = status, FromBIC = "BICX", ToBIC = "TOBIC",
                Message = Encoding.UTF8.GetBytes("original signed payment"),
                Reason = status == TransactionStatus.Failed ? "AC04" : "",
                Transactions = new List<Transaction> { new() { Amount = 100, CreditorAccount = "CA" } }
            };
            Rows[row.EndToEndId!] = row;
            return row;
        }

        public OutgoingTransactionHandler Handler() => new(
            new ISO20022Options { BIC = "BICX", Agent = "AGTX", SIPS = "https://unit.test/sips" },
            NullLogger<OutgoingTransactionHandler>.Instance, signer.Object,
            Mock.Of<ISignatureService>(), persistence.Object, new CorrelationService(), sender.Object,
            iso.Object, Mock.Of<IStatusOrchestrator>(), Microsoft.Extensions.Options.Options.Create(new CoreOptions()));
    }
}
