using System.Text;
using Microsoft.EntityFrameworkCore;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Recall = SIPS.Connect.Services.PapssRecallRules;

namespace SIPS.Connect.Services;

/// <summary>
/// Outbound recalls (camt.056) and their answers. ISOLATION: an answer to a recall is only ever applied to the RECALL operation;
/// the recalled payment is never read for update or changed by a pacs.002 or camt.029, even though they carry its TxId and
/// EndToEndId. Only a pacs.004 (funds returned) changes the payment (RETURNED, as before) and also closes the open recall.
/// </summary>
public sealed partial class PapssOperationStore
{
    private static readonly string RecallKind = UpperSnakeEnumConverter<PapssOperationType>.Of(PapssOperationType.Recall);
    private static readonly string RecallPendingOutcome = UpperSnakeEnumConverter<PapssOutcome>.Of(PapssOutcome.RecallPending);
    private static readonly string RecallAcceptedOutcome = UpperSnakeEnumConverter<PapssOutcome>.Of(PapssOutcome.RecallAcceptedByPapss);
    /// <summary>PAPSS-confirmed 2026-09-26: a return within 7 days of settlement carries exactly the original amount.</summary>
    public static readonly TimeSpan ExactUnwindWindow = TimeSpan.FromDays(7);

    public Task<PapssOperation?> FindOutboundRecallAsync(string recallId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Recall && x.RequestMessageId == recallId, ct);

    /// <summary>The OPEN recall (RECALL_PENDING / RECALL_ACCEPTED_BY_PAPSS) of a payment; at most one exists (ux_papss_op_open_recall).</summary>
    public Task<PapssOperation?> FindOpenRecallAsync(Guid paymentId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().FirstOrDefaultAsync(x => x.OriginalOperationId == paymentId && x.Operation == PapssOperationType.Recall
            && (x.PapssOutcome == PapssOutcome.RecallPending || x.PapssOutcome == PapssOutcome.RecallAcceptedByPapss), ct);

    /// <summary>OUTBOUND payments with this EndToEndId (a recall may name the payment by its EndToEndId).</summary>
    public Task<List<PapssOperation>> FindOutboundPaymentsByEndToEndIdAsync(string endToEndId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().Where(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Payment && x.EndToEndId == endToEndId).Take(2).ToListAsync(ct);

    /// <summary>Whether a payment this participant RECEIVED carries this TxId / EndToEndId (only the debtor agent may recall).</summary>
    public Task<bool> HasInboundPaymentAsync(string? txId, string? endToEndId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().AnyAsync(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Payment
            && (txId != null && x.TxId == txId || txId == null && x.EndToEndId == endToEndId), ct);

    /// <summary>
    /// Inserts the recall (RECALL_PENDING, SUBMITTING, signed camt.056 bytes) before the gateway is called. Created=false returns
    /// the recall already stored under this recall id. A concurrent second open recall for the payment is refused by
    /// ux_papss_op_open_recall and reported as RECALL_ALREADY_OPEN.
    /// </summary>
    public async Task<(PapssOperation Operation, bool Created)> CreateOutboundRecallAsync(PapssSignedMessage signed, PapssOperation payment, string reason, string fingerprint, CancellationToken ct)
    {
        var now = Now;
        var operation = new PapssOperation
        {
            Direction = PapssDirection.Outbound,
            Operation = PapssOperationType.Recall,
            RequestMessageId = signed.BusinessMessageId,
            MsgId = signed.MessageId ?? signed.BusinessMessageId,
            OriginalOperationId = payment.Id,
            OriginalTxId = payment.TxId,
            OriginalEndToEndId = payment.EndToEndId,
            CounterpartyBic = payment.CounterpartyBic,
            Amount = payment.Amount,
            Currency = payment.Currency,
            LocalInstrument = payment.LocalInstrument,
            Reason = reason,
            GatewayState = PapssGatewayState.Submitting,
            PapssOutcome = PapssOutcome.RecallPending,
            BankDeliveryState = PapssDeliveryState.NotRequired,
            SignedRequest = Encoding.UTF8.GetBytes(signed.SignedXml),
            RequestFingerprint = fingerprint,
            SourceCreatedAt = signed.CreatedAt.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PapssOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return (operation, true);
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            db.ChangeTracker.Clear();
            if (await FindOutboundRecallAsync(signed.BusinessMessageId, ct) is { } same) return (same, false);
            var open = await FindOpenRecallAsync(payment.Id, ct);
            throw new ParticipantRailException("RECALL_ALREADY_OPEN",
                $"Payment {payment.TxId} already has an open recall{(open is null ? string.Empty : " " + open.RequestMessageId)}; only one recall per payment may be open.");
        }
    }

    /// <summary>
    /// Record-only beneficiary response deadline (PAPSS-confirmed 30 days, PapssFacing:Recall:ResponseDeadlineDays): admission time +
    /// the configured days, set once. Nothing happens automatically when it passes; the lookup shows responseOverdue.
    /// </summary>
    public Task RecordRecallDeadlineAsync(Guid id, CancellationToken ct)
    {
        if (RecallDeadline(Now) is not { } deadline) return Task.CompletedTask;
        return db.PapssOperations.Where(x => x.Id == id && x.Operation == PapssOperationType.Recall && x.DeadlineAt == null && x.GatewayState == PapssGatewayState.Admitted)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeadlineAt, deadline), ct);
    }

    private DateTimeOffset? RecallDeadline(DateTimeOffset admittedAt)
        => options.Recall.ResponseDeadlineDays is { } days ? admittedAt.AddDays(days) : null;

    // ----------------------------------------------------------------------------------------
    // pacs.002 answering our camt.056 (OrgnlMsgNmId camt.056.*, OrgnlMsgId = recall id)
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// PAPSS's immediate answer to our camt.056. Correlated ONLY by OrgnlMsgId = recall id (and its OrgnlTxId/OrgnlEndToEndId must be
    /// the recalled payment's). It carries the payment's TxId/EndToEndId but is never applied to (nor correlated with) the payment.
    /// ACCP = accepted for processing (the recall stays open); RJCT closes the recall. Uncorrelated answers are stored, not pushed.
    /// </summary>
    public async Task<PapssIngestResult> IngestRecallStatusAsync(string rawXml, PapssStatusReport report, CancellationToken ct)
    {
        var now = Now;
        await using var transaction = await db.BeginTransactionAsync(ct);
        var recall = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
            WHERE operation = {RecallKind} AND direction = 'OUTBOUND' AND requestmessageid = {report.OriginalMessageId}
            LIMIT 1 FOR UPDATE"), ct);
        string? note = null;
        var correlation = PapssCorrelation.None;
        if (recall is not null)
        {
            if (MatchesRecall(recall, report.OriginalTxId, report.OriginalEndToEndId))
                correlation = PapssCorrelation.MessageId;
            else
            {
                correlation = PapssCorrelation.Mismatch;
                note = $"OrgnlMsgId matches recall {recall.RequestMessageId} but OrgnlTxId/OrgnlEndToEndId do not; not attached";
                recall = null;
            }
        }

        var disposition = recall is null ? PapssEventDisposition.Uncorrelated : Recall.EvaluatePapssStatus(recall.PapssOutcome, report.Status);
        if (recall is not null)
        {
            switch (disposition)
            {
                case PapssEventDisposition.Applied:
                    var next = Recall.OutcomeOfPapssStatus(report.Status)!.Value;
                    recall.PapssOutcome = next;
                    recall.PaymentStatus = report.Status;
                    recall.StatusReasonCode = Truncate(report.ReasonCode, 64);
                    recall.AdditionalInfo = report.AdditionalInfo;
                    recall.StatusAt = now;
                    if (next == PapssOutcome.RecallRejectedByPapss) recall.CompletedAt ??= now;
                    MarkAnswered(recall, now);
                    break;
                case PapssEventDisposition.Conflict:
                    recall.StatusConflict = true;
                    recall.UpdatedAt = now;
                    note = Append(note, $"PAPSS recall status {report.Status} contradicts {UpperSnakeEnumConverter<PapssOutcome>.Of(recall.PapssOutcome)}; not applied");
                    break;
            }
        }

        var pushed = disposition == PapssEventDisposition.Applied;
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            OperationId = recall?.Id,
            EventType = PapssEventTypes.RecallStatus,
            MessageType = PapssPaymentMessages.Pacs002,
            SourceMessageId = report.SourceMessageId,
            RawXml = Encoding.UTF8.GetBytes(rawXml),
            ReceivedAt = now,
            PushState = pushed ? PapssDeliveryState.Pending : PapssDeliveryState.NotRequired,
            PushNextAttemptAt = pushed ? now : null,
            Status = Truncate(report.Status, 8),
            ReasonCode = Truncate(report.ReasonCode, 64),
            Correlation = correlation,
            Disposition = disposition,
            OriginalMessageId = Truncate(report.OriginalMessageId, 128),
            OriginalMessageType = Truncate(report.OriginalMessageType, 32),
            OriginalTxId = Truncate(report.OriginalTxId, 128),
            OriginalEndToEndId = Truncate(report.OriginalEndToEndId, 128),
            RawEvidenceReference = Truncate(report.Provenance?.RawEvidenceReference, 128),
            FieldProvenance = ProvenanceJson(report.Provenance),
            Note = Truncate(note, 512)
        });

        if (!await CommitIngestAsync(transaction, ct))
        {
            logger.LogInformation("PAPSS recall pacs.002 {SourceMessageId} was already stored; not applied or pushed again", report.SourceMessageId);
            return new PapssIngestResult(true, null, correlation, disposition, false, null);
        }
        LogRecallAnswer("pacs.002 " + report.Status, report.SourceMessageId, report.OriginalMessageId, recall, disposition, correlation, note);
        return new PapssIngestResult(false, recall, correlation, disposition, pushed, note);
    }

    // ----------------------------------------------------------------------------------------
    // camt.029 (the beneficiary refused our recall)
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Correlated by RslvdCase/Id = recall id when present (OrgnlTxId/OrgnlEndToEndId must match the recall); without it, by the
    /// single OPEN recall of the outbound payment with OrgnlTxId + OrgnlEndToEndId. Applied RJCR closes the recall as
    /// RECALL_REJECTED_BY_BENEFICIARY (the payment is untouched) and queues CB_RecallResult. Uncorrelated: stored for operators.
    /// </summary>
    public async Task<PapssIngestResult> IngestRecallResolutionAsync(string rawXml, PapssRecallResolution message, CancellationToken ct)
    {
        var now = Now;
        await using var transaction = await db.BeginTransactionAsync(ct);
        PapssOperation? recall = null;
        string? note = null;
        var correlation = PapssCorrelation.None;
        if (message.ResolvedCaseId is { } caseId)
        {
            recall = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
                WHERE operation = {RecallKind} AND direction = 'OUTBOUND' AND requestmessageid = {caseId}
                LIMIT 1 FOR UPDATE"), ct);
            if (recall is null)
                note = $"RslvdCase {caseId} matches no stored recall; not attached";
            else if (MatchesRecall(recall, message.OriginalTxId, message.OriginalEndToEndId))
                correlation = PapssCorrelation.MessageId;
            else
            {
                correlation = PapssCorrelation.Mismatch;
                note = $"RslvdCase matches recall {recall.RequestMessageId} but OrgnlTxId/OrgnlEndToEndId do not; not attached";
                recall = null;
            }
        }
        else
        {
            var candidates = await db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
                WHERE operation = {RecallKind} AND direction = 'OUTBOUND' AND originaltxid = {message.OriginalTxId}
                  AND papssoutcome IN ({RecallPendingOutcome}, {RecallAcceptedOutcome})
                ORDER BY createdat LIMIT 2 FOR UPDATE").ToListAsync(ct);
            candidates = candidates.Where(x => MatchesRecall(x, message.OriginalTxId, message.OriginalEndToEndId)).ToList();
            if (candidates.Count == 1)
            {
                recall = candidates[0];
                correlation = PapssCorrelation.TransactionId;
                note = "no RslvdCase: attached to the open recall of the payment";
            }
            else
                note = candidates.Count == 0 ? "no RslvdCase and no open recall for OrgnlTxId/OrgnlEndToEndId; not attached" : "no RslvdCase and more than one open recall matches; not attached";
        }

        var disposition = recall is null ? PapssEventDisposition.Uncorrelated : Recall.EvaluateResolution(recall.PapssOutcome, message.Confirmation);
        if (recall is not null)
        {
            switch (disposition)
            {
                case PapssEventDisposition.Applied:
                    recall.PapssOutcome = PapssOutcome.RecallRejectedByBeneficiary;
                    recall.PaymentStatus = Truncate(message.Confirmation, 8);
                    recall.StatusReasonCode = Truncate(message.ReasonCode, 64);
                    recall.AdditionalInfo = message.AdditionalInfo;
                    recall.SignedResponse = Encoding.UTF8.GetBytes(rawXml);
                    recall.StatusAt = now;
                    recall.CompletedAt ??= now;
                    MarkAnswered(recall, now);
                    break;
                case PapssEventDisposition.Conflict:
                    recall.StatusConflict = true;
                    recall.UpdatedAt = now;
                    note = Append(note, $"camt.029 {message.Confirmation} after {UpperSnakeEnumConverter<PapssOutcome>.Of(recall.PapssOutcome)}; not applied");
                    break;
                case PapssEventDisposition.UnknownStatus:
                    note = Append(note, $"camt.029 confirmation {message.Confirmation ?? "(none)"} is not RJCR; not applied");
                    break;
            }
        }

        var pushed = disposition == PapssEventDisposition.Applied;
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            OperationId = recall?.Id,
            EventType = PapssEventTypes.RecallResolution,
            MessageType = PapssRecallMessages.Camt029,
            SourceMessageId = message.SourceMessageId,
            RawXml = Encoding.UTF8.GetBytes(rawXml),
            ReceivedAt = now,
            PushState = pushed ? PapssDeliveryState.Pending : PapssDeliveryState.NotRequired,
            PushNextAttemptAt = pushed ? now : null,
            Status = Truncate(message.Confirmation, 8),
            ReasonCode = Truncate(message.ReasonCode, 64),
            Correlation = correlation,
            Disposition = disposition,
            OriginalMessageId = Truncate(message.OriginalMessageId, 128),
            OriginalMessageType = Truncate(message.OriginalMessageType, 32),
            OriginalTxId = Truncate(message.OriginalTxId, 128),
            OriginalEndToEndId = Truncate(message.OriginalEndToEndId, 128),
            RawEvidenceReference = Truncate(message.Provenance?.RawEvidenceReference, 128),
            FieldProvenance = ProvenanceJson(message.Provenance),
            Note = Truncate(note, 512)
        });

        if (!await CommitIngestAsync(transaction, ct))
        {
            logger.LogInformation("PAPSS camt.029 {SourceMessageId} was already stored; not applied or pushed again", message.SourceMessageId);
            return new PapssIngestResult(true, null, correlation, disposition, false, null);
        }
        LogRecallAnswer("camt.029 " + message.Confirmation, message.SourceMessageId, message.ResolvedCaseId, recall, disposition, correlation, note);
        return new PapssIngestResult(false, recall, correlation, disposition, pushed, note);
    }

    // ----------------------------------------------------------------------------------------
    // pacs.004 received while a recall is open (called inside IngestInboundReturnAsync's transaction)
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// A pacs.004 does not echo our recall id: it is attributed to the payment's single OPEN recall, which becomes RECALL_RETURNED
    /// (RECALL_RETURNED event, CB_RecallResult queued). Without an open recall the return is spontaneous and nothing is added.
    /// </summary>
    private async Task<(PapssOperation Recall, string Note)?> CloseOpenRecallByReturnAsync(PapssOperation payment, PapssReturnMessage message, byte[] raw, DateTimeOffset now, CancellationToken ct)
    {
        var recall = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
            WHERE operation = {RecallKind} AND direction = 'OUTBOUND' AND originaloperationid = {payment.Id}
              AND papssoutcome IN ({RecallPendingOutcome}, {RecallAcceptedOutcome})
            LIMIT 1 FOR UPDATE"), ct);
        if (recall is null) return null;
        var note = $"pacs.004 RtrId {message.ReturnId} attributed to the open recall of payment {payment.TxId}";
        // PAPSS-confirmed 2026-09-26: returned within 7 days of settlement, the amount is unwound exactly (no fee deduction).
        // A different amount is recorded, never blocking; beyond 7 days the fee treatment is NOT ESTABLISHED.
        if (PapssFieldProvenance.IsReportedAmount(message.AmountSource) && message.Amount is { } returned && payment.Amount is { } original
            && (returned != original || message.Currency is { } ccy && payment.Currency is { } storedCcy && !string.Equals(ccy, storedCcy, StringComparison.OrdinalIgnoreCase)))
        {
            var settledAt = payment.CompletedAt ?? payment.StatusAt ?? payment.CreatedAt;
            note += now - settledAt <= ExactUnwindWindow
                ? $"; returned {returned} {message.Currency} differs from the original {original} {payment.Currency} although returned within 7 days of settlement (PAPSS: exact unwind)"
                : $"; returned {returned} {message.Currency} differs from the original {original} {payment.Currency} (more than 7 days after settlement: fee treatment NOT ESTABLISHED)";
        }
        recall.PapssOutcome = PapssOutcome.RecallReturned;
        recall.ReturnId = message.ReturnId;
        recall.StatusReasonCode = Truncate(message.ReasonCode, 64);
        recall.AdditionalInfo = message.AdditionalInfo;
        recall.SignedResponse = raw;
        recall.StatusAt = now;
        recall.CompletedAt ??= now;
        MarkAnswered(recall, now);
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            OperationId = recall.Id,
            EventType = PapssEventTypes.RecallReturned,
            MessageType = PapssPaymentMessages.Pacs004,
            SourceMessageId = message.SourceMessageId,
            RawXml = raw,
            ReceivedAt = now,
            PushState = PapssDeliveryState.Pending,
            PushNextAttemptAt = now,
            ReasonCode = Truncate(message.ReasonCode, 64),
            Correlation = PapssCorrelation.TransactionId,
            Disposition = PapssEventDisposition.Applied,
            OriginalMessageId = Truncate(message.OriginalMessageId, 128),
            OriginalMessageType = Truncate(message.OriginalMessageType, 32),
            OriginalTxId = Truncate(message.OriginalTxId, 128),
            OriginalEndToEndId = Truncate(message.OriginalEndToEndId, 128),
            Amount = message.Amount,
            Currency = message.Currency,
            AmountSource = message.AmountSource,
            RawEvidenceReference = Truncate(message.Provenance?.RawEvidenceReference, 128),
            FieldProvenance = ProvenanceJson(message.Provenance),
            Note = Truncate(note, 512)
        });
        return (recall, note);
    }

    // ----------------------------------------------------------------------------------------

    private static bool MatchesRecall(PapssOperation recall, string? txId, string? endToEndId)
        => string.Equals(recall.OriginalTxId, txId, StringComparison.Ordinal)
           && (endToEndId is null || recall.OriginalEndToEndId is null || string.Equals(recall.OriginalEndToEndId, endToEndId, StringComparison.Ordinal));

    /// <summary>An answer proves PAPSS got the camt.056 even if the submission outcome was ambiguous; the bank is told.</summary>
    private void MarkAnswered(PapssOperation recall, DateTimeOffset now)
    {
        if (recall.GatewayState is PapssGatewayState.Submitting or PapssGatewayState.SubmissionUnknown)
        {
            recall.GatewayState = PapssGatewayState.Admitted;
            recall.DeadlineAt ??= RecallDeadline(now);
        }
        recall.BankDeliveryState = PapssDeliveryState.Pending;
        recall.UpdatedAt = now;
    }

    /// <summary>Commits; false when the event (type + source message id) was already stored.</summary>
    private async Task<bool> CommitIngestAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private void LogRecallAnswer(string what, string sourceMessageId, string? reference, PapssOperation? recall, string disposition, string correlation, string? note)
    {
        if (recall is null)
            logger.LogWarning("PAPSS recall answer {What} {SourceMessageId} (reference {Reference}) matches no stored recall ({Correlation}); stored UNCORRELATED for operators, not pushed. {Note}",
                what, sourceMessageId, reference, correlation, note);
        else if (disposition == PapssEventDisposition.Conflict)
            logger.LogError("PAPSS recall answer {What} {SourceMessageId} conflicts with recall {RecallId}: {Note}. The recall is flagged statusConflict=true.", what, sourceMessageId, recall.RequestMessageId, note);
        else
            logger.LogInformation("PAPSS recall answer {What} {SourceMessageId} for recall {RecallId}: {Disposition} via {Correlation}; recall now {Outcome}",
                what, sourceMessageId, recall.RequestMessageId, disposition, correlation, UpperSnakeEnumConverter<PapssOutcome>.Of(recall.PapssOutcome));
    }
}
