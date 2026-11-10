using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Gateway;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Persistence;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;

namespace SIPS.Connect.PostgresTests;

[Trait("Category", "Postgres")]
public sealed class OutgoingPaymentIdempotencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_first_inserts_submit_once_and_losing_context_remains_usable(bool bankSuppliesTxId)
    {
        await using var h = await PostgresHarness.CreateAsync();
        await using var provider = h.BuildProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<StorageBroker>();
        var db2 = scope2.ServiceProvider.GetRequiredService<StorageBroker>();
        var both = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0, sends = 0;
        async Task BeforeInsert()
        {
            if (Interlocked.Increment(ref arrivals) == 2) both.TrySetResult(true);
            await both.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var sender = Sender(() => Interlocked.Increment(ref sends));
        var a = Handler(db1, sender, BeforeInsert);
        var b = Handler(db2, sender, BeforeInsert);
        var first = Request(); var second = Request();
        if (bankSuppliesTxId) first.TxId = second.TxId = "BANK-SUPPLIED-TX";
        var results = await Task.WhenAll(a.HandleAsync(first, CancellationToken.None),
            b.HandleAsync(second, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, r => { Assert.True(r.IsSuccess); Assert.Equal("PDNG", r.Data?.Status); });
        Assert.Equal(1, sends);
        Assert.Equal(1, await db1.ISOMessages.CountAsync());
        Assert.Equal(1, await db1.Transactions.CountAsync());
        Assert.DoesNotContain(db1.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        Assert.DoesNotContain(db2.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        // A losing insert must not be reattempted by a subsequent SaveChanges.
        await db1.SaveChangesAsync(CancellationToken.None);
        await db2.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task New_process_replays_original_after_timeout_and_later_final_status_without_sending()
    {
        await using var h = await PostgresHarness.CreateAsync();
        int sends = 0;
        var sender = Sender(() => Interlocked.Increment(ref sends));
        await using (var firstProcess = h.BuildProvider())
        {
            using var scope = firstProcess.CreateScope();
            var result = await Handler(scope.ServiceProvider.GetRequiredService<StorageBroker>(), sender)
                .HandleAsync(Request(), CancellationToken.None);
            Assert.Equal("PDNG", result.Data?.Status);
        }
        await using (var secondProcess = h.BuildProvider())
        {
            using var scope = secondProcess.CreateScope();
            var result = await Handler(scope.ServiceProvider.GetRequiredService<StorageBroker>(), sender)
                .HandleAsync(Request(), CancellationToken.None);
            Assert.Equal("PDNG", result.Data?.Status);
            Assert.Equal(1, sends);
        }
        // Model a subsequently reconciled network outcome.
        await h.WithStorageAsync(async db =>
        {
            var row = await db.ISOMessages.SingleAsync();
            row.Status = TransactionStatus.Success;
            return await db.SaveChangesAsync(CancellationToken.None);
        });
        await using var thirdProcess = h.BuildProvider();
        using var thirdScope = thirdProcess.CreateScope();
        var changed = Request(); changed.Amount = 999; changed.CreditorAccount = "OTHER";
        var replay = await Handler(thirdScope.ServiceProvider.GetRequiredService<StorageBroker>(), sender)
            .HandleAsync(changed, CancellationToken.None);
        Assert.True(replay.IsSuccess);
        Assert.Equal("ACSC", replay.Data?.Status);
        Assert.Equal("LOCAL-123", replay.Data?.EndToEndId);
        Assert.Contains("Original", replay.Data?.AdditionalInfo ?? "");
        Assert.Equal(1, sends);
        await h.WithStorageAsync(async db =>
        {
            var transaction = await db.Transactions.SingleAsync();
            Assert.Equal(100m, transaction.Amount);
            Assert.Equal("CA", transaction.CreditorAccount);
            return 0;
        });
    }

    [Theory]
    [InlineData("FOREIGN", null)]
    [InlineData("BANK", "papss-sec-v1")]
    public async Task Existing_inbound_or_other_rail_does_not_claim_domestic_localId(string from, string? service)
    {
        await using var h = await PostgresHarness.CreateAsync();
        await h.WithStorageAsync(async db =>
        {
            db.ISOMessages.Add(new ISOMessage
            {
                TxId = "BANK-123", EndToEndId = "LOCAL-123", MessageType = ISOMessageType.TransactionRequest,
                FromBIC = from, ToBIC = "OTHER", BusinessService = service,
                MsgId = "ORIGINAL", BizMsgIdr = "ORIGINAL", MsgDefIdr = "pacs.008.001.10",
                Date = DateTimeOffset.UtcNow, Status = TransactionStatus.Success, Message = [1]
            });
            return await db.SaveChangesAsync(CancellationToken.None);
        });
        int sends = 0;
        await using var provider = h.BuildProvider();
        using var scope = provider.CreateScope();
        var result = await Handler(scope.ServiceProvider.GetRequiredService<StorageBroker>(), Sender(() => sends++))
            .HandleAsync(Request(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("PDNG", result.Data?.Status);
        Assert.Equal(1, sends);
    }

    [Theory]
    [InlineData(1, HttpStatusCode.OK)]
    [InlineData(2, HttpStatusCode.Conflict)]
    public async Task Historical_localId_replays_only_when_unambiguous(int count, HttpStatusCode expected)
    {
        await using var h = await PostgresHarness.CreateAsync();
        await h.WithStorageAsync(async db =>
        {
            for (var i = 0; i < count; i++) db.ISOMessages.Add(new ISOMessage
            {
                TxId = $"OLD-{i}", EndToEndId = "LOCAL-123", FromBIC = "BANK", ToBIC = "OTHER",
                MessageType = ISOMessageType.TransactionRequest, Status = TransactionStatus.Success,
                MsgId = $"OLD-{i}", BizMsgIdr = $"OLD-{i}", MsgDefIdr = "pacs.008.001.10",
                Date = DateTimeOffset.UtcNow, Message = [1]
                // No DomesticRequestId: this represents pre-migration records.
            });
            return await db.SaveChangesAsync(CancellationToken.None);
        });
        await using var provider = h.BuildProvider();
        using var scope = provider.CreateScope();
        int sends = 0;
        var result = await Handler(scope.ServiceProvider.GetRequiredService<StorageBroker>(), Sender(() => sends++))
            .HandleAsync(Request(), CancellationToken.None);
        Assert.Equal(expected, result.StatusCode);
        if (count == 1)
        {
            Assert.Equal("OLD-0", result.Data?.TxId);
            Assert.Equal("ACSC", result.Data?.Status);
        }
        else Assert.Null(result.Data);
        Assert.Equal(0, sends);
    }

    private static OutgoingTransactionHandler Handler(StorageBroker db, ISipsRequestSender sender, Func<Task>? beforeInsert = null)
    {
        var real = new PersistenceGateway(new IncomingRecorder(NullLogger<IncomingRecorder>.Instance, db));
        // Only the barrier is mocked: inserts, unique violations, graph detachment
        // and lookup all go through the real recorder and independent DbContexts.
        var persistence = new Mock<IPersistenceGateway>();
        persistence.Setup(x => x.GetDomesticOutgoingByLocalIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string id, string bic, CancellationToken ct) => real.GetDomesticOutgoingByLocalIdAsync(id, bic, ct));
        persistence.Setup(x => x.GetISOMessageByTxIdAndTypeAsync(It.IsAny<string>(), It.IsAny<ISOMessageType>(), It.IsAny<CancellationToken>()))
            .Returns((string id, ISOMessageType type, CancellationToken ct) => real.GetISOMessageByTxIdAndTypeAsync(id, type, ct));
        persistence.Setup(x => x.RecordISOMessageAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
            .Returns(async (ISOMessage row, CancellationToken ct) =>
            {
                if (beforeInsert is not null) await beforeInsert();
                return await real.RecordISOMessageAsync(row, ct);
            });
        var signer = new Mock<INativeSigner>();
        signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns((string xml, string _) => xml);
        var iso = new Mock<IISOMessageService>();
        iso.Setup(x => x.MarkForCheckStatusAsync(It.IsAny<ISOMessage>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        return new OutgoingTransactionHandler(new ISO20022Options { BIC = "BANK", Agent = "AGENT", SIPS = "https://test/sips" },
            NullLogger<OutgoingTransactionHandler>.Instance, signer.Object, Mock.Of<ISignatureService>(),
            persistence.Object, new CorrelationService(), sender, iso.Object, Mock.Of<IStatusOrchestrator>(),
            Microsoft.Extensions.Options.Options.Create(new CoreOptions()));
    }

    private static ISipsRequestSender Sender(Action onSend)
    {
        var sender = new Mock<ISipsRequestSender>();
        sender.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .Returns(() => { onSend(); return Task.FromResult(Response<string>.Fail("Response lost", HttpStatusCode.RequestTimeout)); });
        return sender.Object;
    }

    private static PaymentRequestDto Request() => new()
    {
        TxId = null!, EndToEndId = "LOCAL-123", ToBIC = "TOBIC", LocalInstrument = "P2P",
        CategoryPurpose = "ACCT", Amount = 100, Currency = "USD", DebtorName = "D", DebtorAccount = "DA",
        DebtorAccountType = "ACCT", CreditorName = "C", CreditorAccount = "CA", CreditorAccountType = "ACCT",
        CreditorAgentBIC = "CABIC", CreditorIssuer = "C", RemittanceInformation = "Test payment"
    };
}
