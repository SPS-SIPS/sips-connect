using System.Text;
using Microsoft.EntityFrameworkCore;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;

namespace SIPS.Connect.Services;

/// <summary>
/// R2: a counterparty recalling a payment THIS institution RECEIVED (camt.056, Direction=Inbound, Operation=Recall). A
/// deliberately separate state machine from PapssOperationStore.Recalls.cs (R1, our own outbound recall) -- see
/// PapssInboundRecallRules. ISOLATION, same as R1: the recalled payment itself is never mutated by any of this; it changes
/// only through the authoritative return flow (the pacs.002/pacs.004 the bank's own return eventually receives).
/// </summary>
public sealed partial class PapssOperationStore
{
    private static readonly string InboundRecallKind = UpperSnakeEnumConverter<PapssOperationType>.Of(PapssOperationType.Recall);

    /// <summary>The stored inbound recall by its PAPSS source message id (AppHdr BizMsgIdr of the camt.056 notification).</summary>
    public Task<PapssOperation?> FindInboundRecallAsync(string recallId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Recall && x.RequestMessageId == recallId, ct);

    /// <summary>The OPEN inbound recall of a received payment (ux_papss_op_open_recall); at most one exists.</summary>
    public Task<PapssOperation?> FindOpenInboundRecallAsync(Guid paymentId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().FirstOrDefaultAsync(x => x.OriginalOperationId == paymentId && x.Operation == PapssOperationType.Recall
            && PapssInboundRecallRules.OpenOutcomes.Contains(x.PapssOutcome), ct);

    /// <summary>Inbound recalls still awaiting the core bank's decision, oldest first (there is no bank-push notification for this event yet; the bank discovers them here).</summary>
    public Task<List<PapssOperation>> PendingInboundRecallsAsync(int limit, CancellationToken ct)
        => db.PapssOperations.AsNoTracking()
            .Where(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Recall && x.PapssOutcome == PapssOutcome.InboundRecallAwaitingDecision)
            .OrderBy(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);

    /// <summary>
    /// Stores the inbound camt.056 notification (durably, de-duplicated on the PAPSS source message id: a gateway redelivery
    /// of the same recall returns the existing row unchanged -- Created=false). The received payment it names, when found, is
    /// linked by OriginalOperationId; when not found (e.g. an operation-store gap predating this participant's own history)
    /// it is still stored for operator visibility, just unlinked, exactly as R1 does when a recall's original payment is
    /// missing from the store.
    /// </summary>
    public async Task<(PapssOperation Operation, bool Created)> CreateInboundRecallAsync(string rawXml, PapssInboundRecallMessage message, PapssOperation? originalPayment, CancellationToken ct)
    {
        var now = Now;
        var operation = new PapssOperation
        {
            Direction = PapssDirection.Inbound,
            Operation = PapssOperationType.Recall,
            RequestMessageId = message.SourceMessageId,
            MsgId = message.CancellationId,
            OriginalOperationId = originalPayment?.Id,
            OriginalTxId = message.OriginalTxId,
            OriginalEndToEndId = message.OriginalEndToEndId,
            CounterpartyBic = originalPayment?.CounterpartyBic,
            Amount = originalPayment?.Amount,
            Currency = originalPayment?.Currency,
            LocalInstrument = originalPayment?.LocalInstrument,
            Reason = message.ReasonCode,
            GatewayState = PapssGatewayState.NotSubmitted,
            PapssOutcome = PapssOutcome.InboundRecallAwaitingDecision,
            // No bank webhook push is built for this event in this stage (PapssPaymentEventDelivery.BuildAsync has no case
            // for it and would throw); the bank sees a pending inbound recall via GET Recall/Inbound/{recallId} instead.
            BankDeliveryState = PapssDeliveryState.NotRequired,
            SignedRequest = Encoding.UTF8.GetBytes(rawXml),
            SourceCreatedAt = message.SourceCreatedAt,
            ReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PapssOperations.Add(operation);
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            Operation = operation,
            EventType = PapssEventTypes.InboundRecallReceived,
            MessageType = PapssRecallMessages.InboundRecallDefinition,
            SourceMessageId = message.SourceMessageId,
            RawXml = operation.SignedRequest,
            ReceivedAt = now,
            // See the BankDeliveryState comment above: no push body is built for this event type yet.
            PushState = PapssDeliveryState.NotRequired,
            ReasonCode = Truncate(message.ReasonCode, 64),
            Correlation = originalPayment is null ? PapssCorrelation.None : PapssCorrelation.TransactionId,
            Disposition = originalPayment is null ? PapssEventDisposition.Uncorrelated : PapssEventDisposition.Applied,
            OriginalTxId = Truncate(message.OriginalTxId, 128),
            OriginalEndToEndId = Truncate(message.OriginalEndToEndId, 128)
        });
        try
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return (operation, true);
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            db.ChangeTracker.Clear();
            var existing = await db.PapssOperations.AsNoTracking()
                .SingleAsync(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Recall && x.RequestMessageId == message.SourceMessageId, ct);
            return (existing, false);
        }
    }

    /// <summary>
    /// Records the core bank's accept/reject decision. ACCEPT and REJECT are mutually exclusive and apply only from
    /// InboundRecallAwaitingDecision: a decision submitted while the recall is already decided (or closed) is recorded
    /// as an INBOUND_RECALL_DECISION event with Disposition=Conflict but never changes the stored outcome -- the first
    /// decision wins, exactly like R1's recall-answer conflict handling. Idempotent: the SAME decision repeated while still
    /// AwaitingBankDecision is applied again harmlessly (the outcome does not change); EXACT_REPLAY-style short-circuiting of
    /// the actual camt.029/pacs.004 submission itself is the caller's concern (the reject path's own fingerprint, and the
    /// existing ReturnAsync idempotency for accept).
    /// </summary>
    public async Task<PapssInboundRecallDecisionOutcome> RecordInboundRecallDecisionAsync(Guid id, bool accept, string? reasonCode, string? reasonText, CancellationToken ct)
    {
        var now = Now;
        await using var transaction = await db.BeginTransactionAsync(ct);
        var recall = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
            WHERE id = {id} AND operation = {InboundRecallKind} AND direction = 'INBOUND' LIMIT 1 FOR UPDATE"), ct);
        if (recall is null)
        {
            await transaction.RollbackAsync(ct);
            return PapssInboundRecallDecisionOutcome.NotFound;
        }

        var requested = accept ? PapssOutcome.InboundRecallAcceptedByBank : PapssOutcome.InboundRecallRejectedByBank;
        var applies = recall.PapssOutcome == PapssOutcome.InboundRecallAwaitingDecision || recall.PapssOutcome == requested;
        var disposition = applies ? PapssEventDisposition.Applied : PapssEventDisposition.Conflict;
        if (applies)
        {
            recall.PapssOutcome = requested;
            recall.StatusReasonCode = Truncate(reasonCode, 64);
            recall.AdditionalInfo = reasonText;
            recall.StatusAt = now;
            recall.UpdatedAt = now;
        }
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            OperationId = recall.Id,
            EventType = PapssEventTypes.InboundRecallDecision,
            MessageType = "BANK_DECISION",
            // Deterministic, not random: a retried identical decision while still open must not create a second event
            // (and therefore never collides with a genuinely later, contradictory decision, which gets its own timestamp).
            SourceMessageId = $"DECISION-{recall.RequestMessageId}-{UpperSnakeEnumConverter<PapssOutcome>.Of(requested)}-{(applies ? "A" : now.UtcTicks.ToString())}",
            RawXml = [],
            ReceivedAt = now,
            PushState = PapssDeliveryState.NotRequired,
            Status = accept ? "ACCEPT" : "REJECT",
            ReasonCode = Truncate(reasonCode, 64),
            Correlation = PapssCorrelation.None,
            Disposition = disposition,
            Note = Truncate(applies ? null : $"recall is already {UpperSnakeEnumConverter<PapssOutcome>.Of(recall.PapssOutcome)}; this decision was recorded but not applied", 512)
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            // The identical decision was already recorded (deterministic SourceMessageId collision): treat as applied.
            return applies ? PapssInboundRecallDecisionOutcome.Applied : PapssInboundRecallDecisionOutcome.Conflict;
        }
        logger.LogInformation("PAPSS inbound recall {RecallId} decision {Decision}: {Disposition}", recall.RequestMessageId, requested, disposition);
        return applies ? PapssInboundRecallDecisionOutcome.Applied : PapssInboundRecallDecisionOutcome.Conflict;
    }

    /// <summary>The reply (camt.029 reject or pacs.004-via-return accept) was durably admitted by the gateway: terminal, releases the open-recall lock.</summary>
    public Task RecordInboundRecallReplySubmittedAsync(Guid id, CancellationToken ct)
    {
        var now = Now;
        return db.PapssOperations.Where(x => x.Id == id && x.Operation == PapssOperationType.Recall && x.Direction == PapssDirection.Inbound
                && (x.PapssOutcome == PapssOutcome.InboundRecallAcceptedByBank || x.PapssOutcome == PapssOutcome.InboundRecallRejectedByBank || x.PapssOutcome == PapssOutcome.InboundRecallUnresolved))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PapssOutcome, PapssOutcome.InboundRecallReplySubmitted)
                .SetProperty(x => x.GatewayState, PapssGatewayState.Admitted)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    /// <summary>The reply submission's gateway outcome could not be determined (mirrors RECALL_OUTCOME_UNRESOLVED): still OPEN, operator review.</summary>
    public Task RecordInboundRecallUnresolvedAsync(Guid id, string reason, CancellationToken ct)
    {
        var now = Now;
        return db.PapssOperations.Where(x => x.Id == id && x.Operation == PapssOperationType.Recall && x.Direction == PapssDirection.Inbound
                && (x.PapssOutcome == PapssOutcome.InboundRecallAcceptedByBank || x.PapssOutcome == PapssOutcome.InboundRecallRejectedByBank))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PapssOutcome, PapssOutcome.InboundRecallUnresolved)
                .SetProperty(x => x.GatewayState, PapssGatewayState.SubmissionUnknown)
                .SetProperty(x => x.ReasonCode, reason)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }
}

public enum PapssInboundRecallDecisionOutcome { NotFound, Applied, Conflict }

/// <summary>
/// Inbound recall state rules (R2). Open: INBOUND_RECALL_AWAITING_BANK_DECISION, INBOUND_RECALL_ACCEPTED_BY_BANK,
/// INBOUND_RECALL_REJECTED_BY_BANK, INBOUND_RECALL_UNRESOLVED. Final: INBOUND_RECALL_RESPONSE_SUBMITTED. A deliberately
/// separate, smaller state machine from PapssRecallRules (R1): there is no PAPSS-side acknowledgement to track here (PAPSS
/// 2026-09-26: "for the inbound, you just need to acknowledge it" -- no business reply is expected back), so the only
/// question this side ever answers is "has our own reply been durably submitted yet".
/// </summary>
public static class PapssInboundRecallRules
{
    public static readonly PapssOutcome[] OpenOutcomes =
    [
        PapssOutcome.InboundRecallAwaitingDecision,
        PapssOutcome.InboundRecallAcceptedByBank,
        PapssOutcome.InboundRecallRejectedByBank,
        PapssOutcome.InboundRecallUnresolved
    ];

    public static bool IsOpen(PapssOutcome outcome) => OpenOutcomes.Contains(outcome);
}
