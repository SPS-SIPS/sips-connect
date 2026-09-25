using System.Text;
using Microsoft.EntityFrameworkCore;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Rules = SIPS.Connect.Services.PapssPaymentStatusRules;

namespace SIPS.Connect.Services;

/// <summary>Outcome of storing a received pacs.002 / pacs.004.</summary>
public sealed record PapssIngestResult(bool Duplicate, PapssOperation? Operation, string Correlation, string Disposition, bool Pushed, string? Note);

/// <summary>Payments, returns and status enquiries (Phase 2). Every method commits before returning.</summary>
public sealed partial class PapssOperationStore
{
    private static readonly string PaymentKind = UpperSnakeEnumConverter<PapssOperationType>.Of(PapssOperationType.Payment);
    private static readonly string ReturnKind = UpperSnakeEnumConverter<PapssOperationType>.Of(PapssOperationType.Return);

    // ----------------------------------------------------------------------------------------
    // Outbound payment / return / status enquiry (bank -> SIPS Connect -> gateway)
    // ----------------------------------------------------------------------------------------

    public Task<PapssOperation?> FindOutboundPaymentByTxIdAsync(string txId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Payment && x.TxId == txId, ct);

    public Task<PapssOperation?> FindOutboundReturnAsync(string returnId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Return && x.ReturnId == returnId, ct);

    /// <summary>The payment a return or status enquiry refers to. A received payment wins: returning what we received is the common case.</summary>
    public async Task<PapssOperation?> FindOriginalPaymentAsync(string txId, string? endToEndId, bool preferInbound, CancellationToken ct)
    {
        var rows = await db.PapssOperations.AsNoTracking()
            .Where(x => x.Operation == PapssOperationType.Payment && x.TxId == txId)
            .ToListAsync(ct);
        return rows
            .Where(x => string.IsNullOrEmpty(endToEndId) || x.EndToEndId is null || x.EndToEndId == endToEndId)
            .OrderBy(x => (x.Direction == PapssDirection.Inbound) == preferInbound ? 0 : 1)
            .FirstOrDefault();
    }

    /// <summary>Inserts the payment before the gateway is called. Created=false returns the row already stored for this TxId.</summary>
    public Task<(PapssOperation Operation, bool Created)> CreateOutboundPaymentAsync(PapssSignedMessage signed, PaymentRequestDto request, string fingerprint, CancellationToken ct)
    {
        var now = Now;
        var txId = request.TxId!;
        return InsertOrGetAsync(new PapssOperation
        {
            Direction = PapssDirection.Outbound,
            Operation = PapssOperationType.Payment,
            RequestMessageId = signed.BusinessMessageId,
            MsgId = signed.MessageId,
            TxId = txId,
            EndToEndId = request.EndToEndId,
            CounterpartyBic = request.ToBIC?.Trim().ToUpperInvariant(),
            AccountId = request.CreditorAccount,
            AccountType = request.CreditorAccountType,
            Amount = request.Amount,
            Currency = request.Currency?.Trim().ToUpperInvariant(),
            LocalInstrument = request.LocalInstrument?.Trim().ToUpperInvariant(),
            GatewayState = PapssGatewayState.Submitting,
            PapssOutcome = PapssOutcome.Pending,
            BankDeliveryState = PapssDeliveryState.NotRequired,
            SignedRequest = Encoding.UTF8.GetBytes(signed.SignedXml),
            RequestFingerprint = fingerprint,
            SourceCreatedAt = signed.CreatedAt.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        }, query => query.Where(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Payment && x.TxId == txId), ct);
    }

    public Task<(PapssOperation Operation, bool Created)> CreateOutboundReturnAsync(PapssSignedMessage signed, ReturnPaymentRequestDto request, string fingerprint, PapssOperation? original, CancellationToken ct)
    {
        var now = Now;
        var returnId = request.ReturnId;
        return InsertOrGetAsync(new PapssOperation
        {
            Direction = PapssDirection.Outbound,
            Operation = PapssOperationType.Return,
            RequestMessageId = signed.BusinessMessageId,
            MsgId = signed.MessageId,
            ReturnId = returnId,
            OriginalOperationId = original?.Id,
            OriginalTxId = request.OriginalTxId,
            OriginalEndToEndId = request.OriginalEndToEndId,
            CounterpartyBic = request.ToBIC?.Trim().ToUpperInvariant(),
            Amount = request.OriginalAmount,
            Currency = request.OriginalCurrency?.Trim().ToUpperInvariant(),
            LocalInstrument = request.LocalInstrument?.Trim().ToUpperInvariant(),
            Reason = request.Reason,
            GatewayState = PapssGatewayState.Submitting,
            PapssOutcome = PapssOutcome.Pending,
            BankDeliveryState = PapssDeliveryState.NotRequired,
            SignedRequest = Encoding.UTF8.GetBytes(signed.SignedXml),
            RequestFingerprint = fingerprint,
            SourceCreatedAt = signed.CreatedAt.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        }, query => query.Where(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Return && x.ReturnId == returnId), ct);
    }

    public async Task<PapssOperation> CreateStatusEnquiryAsync(PapssSignedMessage signed, StatusRequestDto request, PapssOperation? original, CancellationToken ct)
    {
        var now = Now;
        var (operation, _) = await InsertOrGetAsync(new PapssOperation
        {
            Direction = PapssDirection.Outbound,
            Operation = PapssOperationType.StatusEnquiry,
            RequestMessageId = signed.BusinessMessageId,
            MsgId = signed.MessageId,
            OriginalOperationId = original?.Id,
            OriginalTxId = request.TxId,
            OriginalEndToEndId = request.EndToEnd,
            CounterpartyBic = request.ToBIC?.Trim().ToUpperInvariant(),
            GatewayState = PapssGatewayState.Submitting,
            PapssOutcome = PapssOutcome.Pending,
            BankDeliveryState = PapssDeliveryState.NotRequired,
            SignedRequest = Encoding.UTF8.GetBytes(signed.SignedXml),
            SourceCreatedAt = signed.CreatedAt.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        }, query => query.Where(x => x.Direction == PapssDirection.Outbound && x.RequestMessageId == signed.BusinessMessageId), ct);
        return operation;
    }

    private async Task<(PapssOperation Operation, bool Created)> InsertOrGetAsync(PapssOperation operation, Func<IQueryable<PapssOperation>, IQueryable<PapssOperation>> existing, CancellationToken ct)
    {
        db.PapssOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return (operation, true);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException error) when (IsUniqueViolation(error))
        {
            db.ChangeTracker.Clear();
            var stored = await existing(db.PapssOperations.AsNoTracking()).FirstOrDefaultAsync(ct)
                ?? await db.PapssOperations.AsNoTracking().FirstAsync(x => x.Direction == operation.Direction && x.RequestMessageId == operation.RequestMessageId, ct);
            return (stored, false);
        }
    }

    // ----------------------------------------------------------------------------------------
    // pacs.002 received for a payment / return / status enquiry (gateway -> SIPS Connect -> bank)
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Stores one received pacs.002 as an event (de-duplicated on its AppHdr BizMsgIdr), correlates it and applies
    /// the status to the operation when it advances it. Correlation: OrgnlMsgId (+ OrgnlMsgNmId selecting payment vs
    /// return) is primary; OrgnlTxId + OrgnlEndToEndId is the secondary key. A message-id match whose TxId/EndToEndId
    /// differ is never attached. Uncorrelated reports are stored (operator-visible) and not pushed.
    /// </summary>
    public async Task<PapssIngestResult> IngestPaymentStatusAsync(string rawXml, PapssStatusReport report, CancellationToken ct)
    {
        var now = Now;
        var settledReturn = options.Returns.SettledStatusList();
        var kinds = KindsFor(report.OriginalMessageType);
        await using var transaction = await db.BeginTransactionAsync(ct);

        PapssOperation? operation = null;
        var correlation = PapssCorrelation.None;
        string? note = null;
        foreach (var kind in kinds)
        {
            var candidate = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
                WHERE operation = {kind} AND (msgid = {report.OriginalMessageId} OR requestmessageid = {report.OriginalMessageId})
                ORDER BY CASE WHEN direction = 'OUTBOUND' THEN 0 ELSE 1 END, createdat LIMIT 1 FOR UPDATE"), ct);
            if (candidate is null) continue;
            if (Matches(candidate, report))
            {
                operation = candidate;
                correlation = PapssCorrelation.MessageId;
            }
            else
            {
                correlation = PapssCorrelation.Mismatch;
                note = $"OrgnlMsgId matches {UpperSnakeEnumConverter<PapssOperationType>.Of(candidate.Operation)} {candidate.RequestMessageId} but OrgnlTxId/OrgnlEndToEndId do not; not attached";
            }
            break;
        }

        if (operation is null && correlation == PapssCorrelation.None && report.OriginalEndToEndId is { } endToEndId)
        {
            foreach (var kind in kinds)
            {
                var candidates = kind == ReturnKind
                    ? await db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
                        WHERE operation = {kind} AND direction = 'OUTBOUND' AND originaltxid = {report.OriginalTxId} AND originalendtoendid = {endToEndId}
                        ORDER BY createdat LIMIT 2 FOR UPDATE").ToListAsync(ct)
                    : await db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
                        WHERE operation = {kind} AND txid = {report.OriginalTxId} AND endtoendid = {endToEndId}
                        ORDER BY CASE WHEN direction = 'OUTBOUND' THEN 0 ELSE 1 END, createdat LIMIT 1 FOR UPDATE").ToListAsync(ct);
                if (candidates.Count == 1)
                {
                    operation = candidates[0];
                    correlation = PapssCorrelation.TransactionId;
                    break;
                }
                if (candidates.Count > 1)
                {
                    note = "OrgnlTxId/OrgnlEndToEndId match more than one outbound return; not attached";
                    break;
                }
            }
        }

        var disposition = operation is null
            ? PapssEventDisposition.Uncorrelated
            : Rules.Evaluate(operation.PapssOutcome, operation.PaymentStatus, report.Status, operation.Operation == PapssOperationType.Return, settledReturn);

        if (operation is not null)
        {
            if (report.Amount is { } amount && operation.Amount is { } stored && amount != stored)
                note = Append(note, $"reported amount {amount} differs from stored {stored}");
            if (report.Currency is { } currency && operation.Currency is { } storedCurrency && !string.Equals(currency, storedCurrency, StringComparison.OrdinalIgnoreCase))
                note = Append(note, $"reported currency {currency} differs from stored {storedCurrency}");

            switch (disposition)
            {
                case PapssEventDisposition.Applied:
                    var next = Rules.OutcomeOf(report.Status, operation.Operation == PapssOperationType.Return, settledReturn)!.Value;
                    operation.PapssOutcome = next;
                    operation.PaymentStatus = report.Status;
                    operation.StatusReasonCode = Truncate(report.ReasonCode, 64);
                    operation.AdditionalInfo = report.AdditionalInfo;
                    operation.StatusAt = now;
                    operation.Amount ??= report.Amount;
                    operation.Currency ??= report.Currency;
                    if (Rules.IsFinal(next)) operation.CompletedAt ??= now;
                    // A PAPSS status proves the gateway admitted our message even if the submission outcome was ambiguous.
                    if (operation.Direction == PapssDirection.Outbound && operation.GatewayState is PapssGatewayState.Submitting or PapssGatewayState.SubmissionUnknown)
                        operation.GatewayState = PapssGatewayState.Admitted;
                    operation.BankDeliveryState = PapssDeliveryState.Pending;
                    operation.UpdatedAt = now;
                    if (operation.Operation == PapssOperationType.Return && next == PapssOutcome.Settled && operation.OriginalOperationId is { } originalId)
                        note = Append(note, await MarkReturnedAsync(originalId, operation.ReturnId, now, ct));
                    break;
                case PapssEventDisposition.Conflict:
                    operation.StatusConflict = true;
                    operation.UpdatedAt = now;
                    note = Append(note, $"conflicting final status {report.Status} after {operation.PaymentStatus ?? "(none)"} ({UpperSnakeEnumConverter<PapssOutcome>.Of(operation.PapssOutcome)}); first final status kept");
                    break;
            }
        }

        var pushed = disposition == PapssEventDisposition.Applied;
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            OperationId = operation?.Id,
            EventType = PapssEventTypes.PaymentStatus,
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
            Amount = report.Amount,
            Currency = report.Currency,
            Note = Truncate(note, 512)
        });

        try
        {
            await db.SaveChangesAsync(ct);
            // Any stored status answers the status enquiries still open for this payment.
            if (operation is { Operation: PapssOperationType.Payment } payment)
                await db.PapssOperations
                    .Where(x => x.OriginalOperationId == payment.Id && x.Operation == PapssOperationType.StatusEnquiry && x.CompletedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.PapssOutcome, payment.PapssOutcome)
                        .SetProperty(x => x.PaymentStatus, payment.PaymentStatus)
                        .SetProperty(x => x.CompletedAt, now)
                        .SetProperty(x => x.UpdatedAt, now), ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            logger.LogInformation("PAPSS pacs.002 {SourceMessageId} was already stored; not applied or pushed again", report.SourceMessageId);
            return new PapssIngestResult(true, null, correlation, disposition, false, null);
        }

        switch (disposition)
        {
            case PapssEventDisposition.Uncorrelated:
                logger.LogWarning("PAPSS pacs.002 {SourceMessageId} (OrgnlMsgId={OriginalMessageId}, OrgnlMsgNmId={OriginalMessageType}, OrgnlTxId={OriginalTxId}, Status={Status}) matches no stored operation ({Correlation}); stored UNCORRELATED, not pushed. {Note}",
                    report.SourceMessageId, report.OriginalMessageId, report.OriginalMessageType, report.OriginalTxId, report.Status, correlation, note);
                break;
            case PapssEventDisposition.Conflict:
                logger.LogError("PAPSS pacs.002 {SourceMessageId} carries a conflicting final status for {RequestMessageId}: {Note}. The operation is flagged statusConflict=true for operator review.",
                    report.SourceMessageId, operation!.RequestMessageId, note);
                break;
            default:
                logger.LogInformation("PAPSS pacs.002 {SourceMessageId} {Status} for {Operation} {RequestMessageId}: {Disposition} via {Correlation}",
                    report.SourceMessageId, report.Status, operation!.Operation, operation.RequestMessageId, disposition, correlation);
                break;
        }
        return new PapssIngestResult(false, operation, correlation, disposition, pushed, note);
    }

    /// <summary>Marks the payment a settled return refers to RETURNED (never over a REJECTED payment: that is flagged).</summary>
    private async Task<string?> MarkReturnedAsync(Guid paymentId, string? returnId, DateTimeOffset now, CancellationToken ct)
    {
        var payment = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($"SELECT *, xmin FROM papss_operations WHERE id = {paymentId} FOR UPDATE"), ct);
        if (payment is null) return null;
        if (payment.PapssOutcome == PapssOutcome.Rejected)
        {
            payment.StatusConflict = true;
            payment.UpdatedAt = now;
            return $"return {returnId} settled against payment {payment.TxId} which is REJECTED; payment flagged";
        }
        if (payment.PapssOutcome == PapssOutcome.Returned) return null;
        payment.PapssOutcome = PapssOutcome.Returned;
        payment.CompletedAt ??= now;
        payment.UpdatedAt = now;
        return null;
    }

    private static bool Matches(PapssOperation operation, PapssStatusReport report)
    {
        var (txId, endToEndId) = operation.Operation == PapssOperationType.Return
            ? (operation.OriginalTxId, operation.OriginalEndToEndId)
            : (operation.TxId, operation.EndToEndId);
        return string.Equals(txId, report.OriginalTxId, StringComparison.Ordinal)
            && (report.OriginalEndToEndId is null || endToEndId is null || string.Equals(endToEndId, report.OriginalEndToEndId, StringComparison.Ordinal));
    }

    /// <summary>OrgnlMsgNmId selects the operation kind; an unknown/missing one tries payment first, then return.</summary>
    private static string[] KindsFor(string? originalMessageType) => originalMessageType switch
    {
        { } type when type.StartsWith("pacs.008", StringComparison.OrdinalIgnoreCase) => [PaymentKind],
        { } type when type.StartsWith("pacs.004", StringComparison.OrdinalIgnoreCase) => [ReturnKind],
        _ => [PaymentKind, ReturnKind]
    };

    private static async Task<PapssOperation?> LockOneAsync(IQueryable<PapssOperation> query, CancellationToken ct)
        => await query.ToListAsync(ct) is { Count: > 0 } rows ? rows[0] : null;

    // ----------------------------------------------------------------------------------------
    // Inbound pacs.004 (a return of a payment this participant sent)
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Stores the inbound return (keyed on the PAPSS source message id and on RtrId), links it to the outbound payment by
    /// OrgnlTxId + OrgnlEndToEndId, marks that payment RETURNED and queues the bank push (CB_ReturnRequest).
    /// </summary>
    public async Task<PapssIngestResult> IngestInboundReturnAsync(string rawXml, PapssReturnMessage message, CancellationToken ct)
    {
        var now = Now;
        var raw = Encoding.UTF8.GetBytes(rawXml);
        await using (var transaction = await db.BeginTransactionAsync(ct))
        {
            var payment = await LockOneAsync(db.PapssOperations.FromSqlInterpolated($@"SELECT *, xmin FROM papss_operations
                WHERE operation = {PaymentKind} AND direction = 'OUTBOUND' AND txid = {message.OriginalTxId} AND endtoendid = {message.OriginalEndToEndId}
                LIMIT 1 FOR UPDATE"), ct);
            string? note = null;
            var disposition = PapssEventDisposition.Uncorrelated;
            if (payment is not null)
            {
                if (payment.PapssOutcome is PapssOutcome.Rejected or PapssOutcome.Returned)
                {
                    disposition = PapssEventDisposition.Conflict;
                    payment.StatusConflict = true;
                    payment.UpdatedAt = now;
                    note = $"return {message.ReturnId} received for payment {payment.TxId} which is already {UpperSnakeEnumConverter<PapssOutcome>.Of(payment.PapssOutcome)}; payment flagged";
                }
                else
                {
                    disposition = PapssEventDisposition.Applied;
                    payment.PapssOutcome = PapssOutcome.Returned;
                    payment.CompletedAt ??= now;
                    payment.UpdatedAt = now;
                }
            }

            var operation = new PapssOperation
            {
                Direction = PapssDirection.Inbound,
                Operation = PapssOperationType.Return,
                RequestMessageId = message.SourceMessageId,
                MsgId = message.MsgId,
                ReturnId = message.ReturnId,
                OriginalOperationId = payment?.Id,
                OriginalTxId = message.OriginalTxId,
                OriginalEndToEndId = message.OriginalEndToEndId,
                CounterpartyBic = message.InstructingAgent,
                Amount = message.Amount,
                Currency = message.Currency,
                LocalInstrument = Truncate(message.LocalInstrument, 35),
                StatusReasonCode = Truncate(message.ReasonCode, 64),
                AdditionalInfo = message.AdditionalInfo,
                GatewayState = PapssGatewayState.NotSubmitted,
                // Evidence §D10: an inbound pacs.004 is acknowledged, not answered with a pacs.002; receiving it means
                // PAPSS processed the return, so it is recorded as SETTLED.
                PapssOutcome = PapssOutcome.Settled,
                BankDeliveryState = PapssDeliveryState.Pending,
                SignedRequest = raw,
                SourceCreatedAt = message.SourceCreatedAt,
                ReceivedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = now
            };
            db.PapssOperations.Add(operation);
            db.PapssOperationEvents.Add(new PapssOperationEvent
            {
                Operation = operation,
                EventType = PapssEventTypes.ReturnReceived,
                MessageType = PapssPaymentMessages.Pacs004,
                SourceMessageId = message.SourceMessageId,
                RawXml = raw,
                ReceivedAt = now,
                // The bank is always told: the funds come back whether or not the payment is in this store.
                PushState = PapssDeliveryState.Pending,
                PushNextAttemptAt = now,
                ReasonCode = Truncate(message.ReasonCode, 64),
                Correlation = payment is null ? PapssCorrelation.None : PapssCorrelation.TransactionId,
                Disposition = disposition,
                OriginalMessageId = Truncate(message.OriginalMessageId, 128),
                OriginalMessageType = Truncate(message.OriginalMessageType, 32),
                OriginalTxId = Truncate(message.OriginalTxId, 128),
                OriginalEndToEndId = Truncate(message.OriginalEndToEndId, 128),
                Amount = message.Amount,
                Currency = message.Currency,
                Note = Truncate(note, 512)
            });
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
                if (payment is null)
                    logger.LogWarning("PAPSS pacs.004 {SourceMessageId} (RtrId={ReturnId}) matches no stored outbound payment (OrgnlTxId={OriginalTxId}, OrgnlEndToEndId={OriginalEndToEndId}); stored and pushed to the bank",
                        message.SourceMessageId, message.ReturnId, message.OriginalTxId, message.OriginalEndToEndId);
                else if (note is not null)
                    logger.LogError("PAPSS pacs.004 {SourceMessageId}: {Note}", message.SourceMessageId, note);
                return new PapssIngestResult(false, operation, payment is null ? PapssCorrelation.None : PapssCorrelation.TransactionId, disposition, true, note);
            }
            catch (DbUpdateException error) when (IsUniqueViolation(error))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
        }

        // Redelivery: the same source message (nothing to do) or the same RtrId under a new source message id
        // (kept for audit on the existing return, not pushed again).
        if (await db.PapssOperationEvents.AsNoTracking().AnyAsync(x => x.EventType == PapssEventTypes.ReturnReceived && x.SourceMessageId == message.SourceMessageId, ct))
        {
            logger.LogInformation("PAPSS pacs.004 {SourceMessageId} was already stored; not pushed again", message.SourceMessageId);
            return new PapssIngestResult(true, null, PapssCorrelation.None, PapssEventDisposition.DuplicateFinal, false, null);
        }
        var existing = await db.PapssOperations.AsNoTracking()
            .FirstAsync(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Return && x.ReturnId == message.ReturnId, ct);
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            OperationId = existing.Id,
            EventType = PapssEventTypes.ReturnReceived,
            MessageType = PapssPaymentMessages.Pacs004,
            SourceMessageId = message.SourceMessageId,
            RawXml = raw,
            ReceivedAt = now,
            PushState = PapssDeliveryState.NotRequired,
            Correlation = PapssCorrelation.TransactionId,
            Disposition = PapssEventDisposition.DuplicateFinal,
            OriginalTxId = Truncate(message.OriginalTxId, 128),
            OriginalEndToEndId = Truncate(message.OriginalEndToEndId, 128),
            ReasonCode = Truncate(message.ReasonCode, 64),
            Amount = message.Amount,
            Currency = message.Currency,
            Note = $"RtrId {message.ReturnId} already received as {existing.RequestMessageId}"
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (IsUniqueViolation(error)) { }
        db.ChangeTracker.Clear();
        logger.LogWarning("PAPSS pacs.004 {SourceMessageId} repeats RtrId {ReturnId} already stored as {RequestMessageId}; kept for audit, not pushed", message.SourceMessageId, message.ReturnId, existing.RequestMessageId);
        return new PapssIngestResult(true, existing, PapssCorrelation.TransactionId, PapssEventDisposition.DuplicateFinal, false, null);
    }

    // ----------------------------------------------------------------------------------------
    // Inbound pacs.008 (credit transfer received from PAPSS)
    // ----------------------------------------------------------------------------------------

    /// <summary>Records the inbound payment (de-duplicated on the PAPSS source message id and on TxId) and links the isomessages decision.</summary>
    public async Task<PapssOperation> RecordInboundPaymentAsync(string rawXml, PapssPaymentMessage message, CancellationToken ct)
    {
        var now = Now;
        var raw = Encoding.UTF8.GetBytes(rawXml);
        var operation = new PapssOperation
        {
            Direction = PapssDirection.Inbound,
            Operation = PapssOperationType.Payment,
            RequestMessageId = message.SourceMessageId,
            MsgId = message.MsgId,
            TxId = message.TxId,
            EndToEndId = message.EndToEndId,
            CounterpartyBic = message.DebtorAgent,
            Amount = message.Amount,
            Currency = message.Currency,
            LocalInstrument = Truncate(message.LocalInstrument, 35),
            GatewayState = PapssGatewayState.NotSubmitted,
            PapssOutcome = PapssOutcome.Pending,
            BankDeliveryState = PapssDeliveryState.Pending,
            SignedRequest = raw,
            SourceCreatedAt = message.SourceCreatedAt,
            ReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PapssOperations.Add(operation);
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            Operation = operation,
            EventType = PapssEventTypes.PaymentReceived,
            MessageType = PapssPaymentMessages.Pacs008,
            SourceMessageId = message.SourceMessageId,
            RawXml = raw,
            ReceivedAt = now,
            // The core bank (CB_PaymentRequest) is called inline by the pacs.008 handler; its answer is the decision.
            PushState = PapssDeliveryState.NotRequired,
            OriginalTxId = Truncate(message.TxId, 128),
            OriginalEndToEndId = Truncate(message.EndToEndId, 128),
            Amount = message.Amount,
            Currency = message.Currency
        });
        try
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            db.ChangeTracker.Clear();
            operation = await db.PapssOperations.AsNoTracking()
                .Where(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Payment && (x.RequestMessageId == message.SourceMessageId || x.TxId == message.TxId))
                .OrderBy(x => x.RequestMessageId == message.SourceMessageId ? 0 : 1)
                .FirstAsync(ct);
            if (operation.RequestMessageId != message.SourceMessageId)
                logger.LogWarning("PAPSS pacs.008 {SourceMessageId} repeats TxId {TxId} already received as {RequestMessageId}", message.SourceMessageId, message.TxId, operation.RequestMessageId);
        }
        await SyncInboundPaymentDecisionAsync(operation.Id, ct);
        return operation;
    }

    /// <summary>
    /// Mirrors the isomessages row of an inbound payment into its operation: the bank decision (CB_PaymentRequest answer,
    /// ACCP/RJCT as the decision publisher maps it) and the PAPSS decision outbox state (gateway state).
    /// A later PAPSS status that already moved the payment on is never overwritten by the decision.
    /// </summary>
    public async Task SyncInboundPaymentDecisionAsync(Guid operationId, CancellationToken ct)
    {
        // A pacs.002 for the same payment may update the row concurrently (xmin): re-read and retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await SyncInboundPaymentDecisionOnceAsync(operationId, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task SyncInboundPaymentDecisionOnceAsync(Guid operationId, CancellationToken ct)
    {
        var now = Now;
        var operation = await db.PapssOperations.SingleAsync(x => x.Id == operationId, ct);
        var iso = await db.ISOMessages.AsNoTracking()
            .Where(x => x.MessageType == ISOMessageType.TransactionRequest && x.TxId == operation.TxId)
            .Select(x => new { x.Id, x.Response, x.PapssDecision, x.PapssDecisionAdmissionCode, x.PapssDecisionPublishedAt, x.PapssDecisionFailureCode, x.PapssDecisionFailedAt })
            .FirstOrDefaultAsync(ct);
        if (iso is null)
        {
            db.ChangeTracker.Clear();
            return;
        }

        operation.IsoMessageId = iso.Id;
        if (iso.Response is { Length: > 0 } response)
        {
            // Only the first time: afterwards bankdeliverystate tracks the pushes of later PAPSS statuses.
            if (operation.PaymentStatus is null && operation.PapssOutcome == PapssOutcome.Pending)
            {
                if (operation.BankDeliveryState == PapssDeliveryState.Pending) operation.BankDeliveryState = PapssDeliveryState.Delivered;
                try
                {
                    var (status, reason) = PapssPaymentMessages.DecisionStatus(Encoding.UTF8.GetString(response));
                    // Same mapping as PapssPaymentDecisionPublisher: anything but RJCT is sent to PAPSS as ACCP.
                    var decision = status == Rules.RJCT ? Rules.RJCT : Rules.ACCP;
                    operation.PaymentStatus = decision;
                    operation.PapssOutcome = decision == Rules.RJCT ? PapssOutcome.Rejected : PapssOutcome.Accepted;
                    operation.StatusReasonCode = decision == Rules.RJCT ? Truncate(reason, 64) : null;
                    operation.StatusAt = now;
                    if (decision == Rules.RJCT) operation.CompletedAt ??= now;
                }
                catch (InvalidDataException error)
                {
                    logger.LogWarning(error, "Bank decision of inbound PAPSS payment {TxId} could not be read", operation.TxId);
                }
            }
        }

        if (iso.PapssDecisionPublishedAt is not null)
        {
            operation.GatewayState = PapssGatewayState.Admitted;
            operation.AdmissionCode = iso.PapssDecisionAdmissionCode;
        }
        else if (iso.PapssDecisionFailedAt is not null)
        {
            operation.GatewayState = PapssGatewayState.Rejected;
            operation.AdmissionCode = iso.PapssDecisionFailureCode;
            operation.ReasonCode = iso.PapssDecisionFailureCode;
        }
        else if (iso.PapssDecision is not null && operation.GatewayState == PapssGatewayState.NotSubmitted)
        {
            operation.GatewayState = PapssGatewayState.Submitting;
        }
        operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ----------------------------------------------------------------------------------------
    // Lookup
    // ----------------------------------------------------------------------------------------

    public Task<PapssOperation?> FindInboundPaymentAsync(string sourceMessageId, string txId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking()
            .Where(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Payment && (x.RequestMessageId == sourceMessageId || x.TxId == txId))
            .OrderBy(x => x.RequestMessageId == sourceMessageId ? 0 : 1)
            .FirstOrDefaultAsync(ct);

    /// <summary>Called by the decision outbox after it published (or terminally failed) the PAPSS decision of an isomessages row.</summary>
    public async Task SyncInboundPaymentDecisionByIsoMessageAsync(int isoMessageId, CancellationToken ct)
    {
        var ids = await db.PapssOperations.AsNoTracking().Where(x => x.IsoMessageId == isoMessageId).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids) await SyncInboundPaymentDecisionAsync(id, ct);
    }

    public Task<PapssOperation?> FindByIdAsync(Guid id, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    /// <summary>The payment with this TxId; the OUTBOUND one (the bank's own TxId) wins over a received one.</summary>
    public async Task<PapssOperation?> FindPaymentAsync(string txId, CancellationToken ct)
        => (await db.PapssOperations.AsNoTracking().Where(x => x.Operation == PapssOperationType.Payment && x.TxId == txId).ToListAsync(ct))
            .OrderBy(x => x.Direction == PapssDirection.Outbound ? 0 : 1).FirstOrDefault();

    public async Task<PapssOperation?> FindReturnAsync(string returnId, CancellationToken ct)
        => (await db.PapssOperations.AsNoTracking().Where(x => x.Operation == PapssOperationType.Return && x.ReturnId == returnId).ToListAsync(ct))
            .OrderBy(x => x.Direction == PapssDirection.Outbound ? 0 : 1).FirstOrDefault();

    public async Task<PapssOperation?> FindPaymentByEndToEndIdAsync(string endToEndId, CancellationToken ct)
        => (await db.PapssOperations.AsNoTracking().Where(x => x.Operation == PapssOperationType.Payment && x.EndToEndId == endToEndId).ToListAsync(ct))
            .OrderBy(x => x.Direction == PapssDirection.Outbound ? 0 : 1).ThenByDescending(x => x.CreatedAt).FirstOrDefault();

    /// <summary>Returns and status enquiries that refer to this operation.</summary>
    public Task<List<PapssOperation>> FindLinkedAsync(Guid operationId, CancellationToken ct)
        => db.PapssOperations.AsNoTracking().Where(x => x.OriginalOperationId == operationId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    /// <summary>Received payment/return/status messages of one operation, oldest first, without the raw XML.</summary>
    public Task<List<PapssOperationEvent>> StatusHistoryAsync(Guid operationId, CancellationToken ct)
        => EventSummaries(db.PapssOperationEvents.AsNoTracking()
                .Where(x => x.OperationId == operationId && x.EventType != PapssEventTypes.VerificationResult && x.EventType != PapssEventTypes.VerificationEnquiry)
                .OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id))
            .ToListAsync(ct);

    /// <summary>Operator view: received pacs.002/pacs.004 that were not attached (uncorrelated / mismatch) or that conflict.</summary>
    public Task<List<PapssOperationEvent>> UnresolvedStatusEventsAsync(int limit, CancellationToken ct)
        => EventSummaries(db.PapssOperationEvents.AsNoTracking()
                .Where(x => (x.EventType == PapssEventTypes.PaymentStatus || x.EventType == PapssEventTypes.ReturnReceived)
                            && (x.Disposition == PapssEventDisposition.Uncorrelated || x.Disposition == PapssEventDisposition.Conflict || x.Disposition == PapssEventDisposition.UnknownStatus))
                .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
                .Take(Math.Clamp(limit, 1, 500)))
            .ToListAsync(ct);

    private static IQueryable<PapssOperationEvent> EventSummaries(IQueryable<PapssOperationEvent> query) => query.Select(x => new PapssOperationEvent
    {
        Id = x.Id,
        OperationId = x.OperationId,
        EventType = x.EventType,
        MessageType = x.MessageType,
        SourceMessageId = x.SourceMessageId,
        ReceivedAt = x.ReceivedAt,
        PushState = x.PushState,
        PushAttempts = x.PushAttempts,
        PushDeliveredAt = x.PushDeliveredAt,
        PushLastError = x.PushLastError,
        Status = x.Status,
        ReasonCode = x.ReasonCode,
        Correlation = x.Correlation,
        Disposition = x.Disposition,
        OriginalMessageId = x.OriginalMessageId,
        OriginalMessageType = x.OriginalMessageType,
        OriginalTxId = x.OriginalTxId,
        OriginalEndToEndId = x.OriginalEndToEndId,
        Amount = x.Amount,
        Currency = x.Currency,
        Note = x.Note
    });

    private static string? Append(string? note, string? addition)
        => string.IsNullOrEmpty(addition) ? note : string.IsNullOrEmpty(note) ? addition : note + "; " + addition;

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
