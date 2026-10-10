using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SIPS.Connect.Services;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Connect.PostgresTests;

/// <summary>
/// R2 inbound recall (camt.056 recalling a payment THIS institution received) against a real PostgreSQL: ingest/idempotency,
/// the accept/reject decision's mutual exclusivity, and the one-open-recall-per-payment index reused from R1.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class PapssInboundRecallOperationTests
{
    [Fact]
    public async Task Migration_widens_the_open_recall_index_to_cover_inbound_outcomes()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var history = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migrationid LIKE '%_AddPapssInboundRecall'", connection);
        Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);
        await using var index = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_papss_op_open_recall'", connection);
        var definition = (string)(await index.ExecuteScalarAsync())!;
        Assert.Contains("INBOUND_RECALL_AWAITING_DECISION", definition);
        Assert.Contains("INBOUND_RECALL_ACCEPTED_BY_BANK", definition);
        Assert.Contains("INBOUND_RECALL_REJECTED_BY_BANK", definition);
        Assert.Contains("INBOUND_RECALL_UNRESOLVED", definition);
        Assert.DoesNotContain("INBOUND_RECALL_REPLY_SUBMITTED", definition);
    }

    [Fact]
    public async Task Ingest_is_durable_linked_to_the_received_payment_and_idempotent_on_redelivery()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-1", "E2E-RX-1");
        var message = InboundRecall("CT02-RX-1-RECALL", "RX-1", "E2E-RX-1", "DUPL");

        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
            var (operation, created) = await store.CreateInboundRecallAsync("<raw/>", message, payment, CancellationToken.None);
            Assert.True(created);
            Assert.Equal(PapssOutcome.InboundRecallAwaitingDecision, operation.PapssOutcome);
            Assert.Equal(payment.Id, operation.OriginalOperationId);
            Assert.Equal("DUPL", operation.Reason);

            // Redelivery of the identical gateway notification (same PAPSS source message id) is idempotent: the same row, not a new one.
            var (replay, createdAgain) = await store.CreateInboundRecallAsync("<raw/>", message, payment, CancellationToken.None);
            Assert.False(createdAgain);
            Assert.Equal(operation.Id, replay.Id);
        }
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Recall)));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.InboundRecallReceived)));
    }

    [Fact]
    public async Task A_recall_naming_a_payment_not_in_the_store_is_still_recorded_unlinked()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var message = InboundRecall("CT02-RX-UNLINKED", "UNKNOWN-TX", "UNKNOWN-E2E", "DUPL");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var (operation, created) = await store.CreateInboundRecallAsync("<raw/>", message, originalPayment: null, CancellationToken.None);
        Assert.True(created);
        Assert.Null(operation.OriginalOperationId);
        Assert.Equal(PapssOutcome.InboundRecallAwaitingDecision, operation.PapssOutcome);
    }

    [Fact]
    public async Task Accept_and_reject_are_mutually_exclusive_a_second_contradictory_decision_is_recorded_but_not_applied()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-2", "E2E-RX-2");
        Guid recallId;
        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
            var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-2-RECALL", "RX-2", "E2E-RX-2", "DUPL"), payment, CancellationToken.None);
            recallId = operation.Id;

            var accepted = await store.RecordInboundRecallDecisionAsync(recallId, accept: true, null, null, CancellationToken.None);
            Assert.Equal(PapssInboundRecallDecisionOutcome.Applied, accepted);

            // A later, contradictory REJECT must not flip an already-decided ACCEPT.
            var contradiction = await store.RecordInboundRecallDecisionAsync(recallId, accept: false, "CUST", "too late", CancellationToken.None);
            Assert.Equal(PapssInboundRecallDecisionOutcome.Conflict, contradiction);
        }
        var stored = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == recallId));
        Assert.Equal(PapssOutcome.InboundRecallAcceptedByBank, stored.PapssOutcome);
        Assert.Null(stored.StatusReasonCode); // the conflicting REJECT's reason must never have been applied
        Assert.Equal(2, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.OperationId == recallId && x.EventType == PapssEventTypes.InboundRecallDecision)));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.OperationId == recallId && x.EventType == PapssEventTypes.InboundRecallDecision && x.Disposition == PapssEventDisposition.Conflict)));
    }

    [Fact]
    public async Task Repeating_the_same_decision_while_still_open_applies_harmlessly()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-3", "E2E-RX-3");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-3-RECALL", "RX-3", "E2E-RX-3", "DUPL"), payment, CancellationToken.None);

        Assert.Equal(PapssInboundRecallDecisionOutcome.Applied, await store.RecordInboundRecallDecisionAsync(operation.Id, accept: false, "CUST", null, CancellationToken.None));
        Assert.Equal(PapssInboundRecallDecisionOutcome.Applied, await store.RecordInboundRecallDecisionAsync(operation.Id, accept: false, "CUST", null, CancellationToken.None));
    }

    [Fact]
    public async Task Response_submitted_is_terminal_and_releases_the_open_recall_lock()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-4", "E2E-RX-4");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-4-RECALL", "RX-4", "E2E-RX-4", "DUPL"), payment, CancellationToken.None);
        Assert.NotNull(await store.FindOpenInboundRecallAsync(payment.Id, CancellationToken.None));

        await store.RecordInboundRecallDecisionAsync(operation.Id, accept: true, null, null, CancellationToken.None);
        await store.RecordInboundRecallReplySubmittedAsync(operation.Id, CancellationToken.None);

        var stored = await store.FindInboundRecallAsync("CT02-RX-4-RECALL", CancellationToken.None);
        Assert.Equal(PapssOutcome.InboundRecallReplySubmitted, stored!.PapssOutcome);
        Assert.NotNull(stored.CompletedAt);
        // The lock is released: a second recall of the SAME payment may now be admitted.
        Assert.Null(await store.FindOpenInboundRecallAsync(payment.Id, CancellationToken.None));
        var (second, created) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-4-SECOND", "RX-4", "E2E-RX-4", "FRAD"), payment, CancellationToken.None);
        Assert.True(created);
        Assert.NotEqual(operation.Id, second.Id);
    }

    [Fact]
    public async Task At_most_one_open_inbound_recall_per_payment_the_index_refuses_a_concurrent_second_one()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-5", "E2E-RX-5");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-5-A", "RX-5", "E2E-RX-5", "DUPL"), payment, CancellationToken.None);

        // A second, distinct PAPSS source message recalling the SAME payment while the first is still open: the partial
        // unique index on originaloperationid refuses it directly at the database level (defence in depth).
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.WithStorageAsync(async db =>
        {
            db.PapssOperations.Add(new PapssOperation
            {
                Direction = PapssDirection.Inbound,
                Operation = PapssOperationType.Recall,
                RequestMessageId = "CT02-RX-5-B",
                OriginalOperationId = payment.Id,
                OriginalTxId = "RX-5",
                OriginalEndToEndId = "E2E-RX-5",
                GatewayState = PapssGatewayState.NotSubmitted,
                PapssOutcome = PapssOutcome.InboundRecallAwaitingDecision,
                BankDeliveryState = PapssDeliveryState.NotRequired,
                SignedRequest = "<raw/>"u8.ToArray(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }));
    }

    private static PapssInboundRecallMessage InboundRecall(string sourceMessageId, string originalTxId, string originalEndToEndId, string reasonCode)
        => new(sourceMessageId, DateTimeOffset.UtcNow, "CXL-" + sourceMessageId, originalTxId, originalEndToEndId, reasonCode, null);

    private static async Task<PapssOperation> SeedReceivedPayment(PostgresHarness harness, string txId, string endToEndId)
    {
        var now = DateTimeOffset.UtcNow;
        return await harness.WithStorageAsync(async db =>
        {
            var payment = new PapssOperation
            {
                Direction = PapssDirection.Inbound,
                Operation = PapssOperationType.Payment,
                RequestMessageId = "CT02-" + txId,
                MsgId = "CT02-" + txId,
                TxId = txId,
                EndToEndId = endToEndId,
                CounterpartyBic = "EGAFEGCX",
                Amount = 500.00m,
                Currency = "USD",
                LocalInstrument = "USDP",
                GatewayState = PapssGatewayState.NotSubmitted,
                PapssOutcome = PapssOutcome.Settled,
                BankDeliveryState = PapssDeliveryState.Delivered,
                SignedRequest = "<raw/>"u8.ToArray(),
                SourceCreatedAt = now,
                ReceivedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PapssOperations.Add(payment);
            await db.SaveChangesAsync(CancellationToken.None);
            return payment;
        });
    }
}
