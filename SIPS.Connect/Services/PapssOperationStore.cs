using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SIPS.Connect.Config;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;

namespace SIPS.Connect.Services;

/// <summary>
/// Durable store for PAPSS operations (papss_operations), received messages (papss_operation_events,
/// which also carries the bank push outbox) and gateway-bound replies (papss_outbound_responses).
/// Every method commits before returning, so callers may acknowledge the gateway afterwards.
/// </summary>
public sealed class PapssOperationStore(IStorageBroker db, PapssFacingOptions options, TimeProvider clock, ILogger<PapssOperationStore> logger)
{
    public const string Acmt024 = "acmt.024.001.03";
    public const string Acmt023 = "acmt.023.001.03";

    private DateTimeOffset Now => clock.GetUtcNow();

    // ----------------------------------------------------------------------------------------
    // Outbound verification (bank -> SIPS Connect -> PAPSS)
    // ----------------------------------------------------------------------------------------

    /// <summary>Inserts the operation before the gateway is called. Returns the existing row when the request message id is reused.</summary>
    public async Task<(PapssOperation Operation, bool Created)> CreateOutboundVerificationAsync(PapssSignedMessage signed, VerificationRequestDto request, CancellationToken ct)
    {
        var now = Now;
        var operation = new PapssOperation
        {
            Direction = PapssDirection.Outbound,
            Operation = PapssOperationType.Verification,
            RequestMessageId = signed.BusinessMessageId,
            VerificationId = signed.BusinessMessageId,
            CounterpartyBic = request.ToBIC?.Trim().ToUpperInvariant(),
            AccountId = request.Alias,
            AccountType = request.Type,
            GatewayState = PapssGatewayState.Submitting,
            PapssOutcome = PapssOutcome.Pending,
            BankDeliveryState = PapssDeliveryState.NotRequired,
            SignedRequest = Encoding.UTF8.GetBytes(signed.SignedXml),
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
            var existing = await db.PapssOperations.AsNoTracking()
                .SingleAsync(x => x.Direction == PapssDirection.Outbound && x.RequestMessageId == signed.BusinessMessageId, ct);
            return (existing, false);
        }
    }

    public Task MarkGatewayAdmittedAsync(Guid id, string admissionCode, CancellationToken ct)
        => db.PapssOperations.Where(x => x.Id == id && x.GatewayState != PapssGatewayState.Rejected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.GatewayState, PapssGatewayState.Admitted)
                .SetProperty(x => x.AdmissionCode, admissionCode)
                .SetProperty(x => x.UpdatedAt, Now), ct);

    public Task MarkGatewayRejectedAsync(Guid id, string code, CancellationToken ct)
    {
        var now = Now;
        return db.PapssOperations.Where(x => x.Id == id && x.GatewayState != PapssGatewayState.Admitted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.GatewayState, PapssGatewayState.Rejected)
                .SetProperty(x => x.PapssOutcome, PapssOutcome.Rejected)
                .SetProperty(x => x.AdmissionCode, code)
                .SetProperty(x => x.ReasonCode, code)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    public Task MarkGatewaySubmissionUnknownAsync(Guid id, string reason, CancellationToken ct)
        => db.PapssOperations.Where(x => x.Id == id && x.GatewayState == PapssGatewayState.Submitting)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.GatewayState, PapssGatewayState.SubmissionUnknown)
                .SetProperty(x => x.ReasonCode, reason)
                .SetProperty(x => x.UpdatedAt, Now), ct);

    // ----------------------------------------------------------------------------------------
    // Asynchronous acmt.024 result (PAPSS -> SIPS Connect -> bank)
    // ----------------------------------------------------------------------------------------

    public async Task<VerificationResultInboxOutcome> IngestVerificationResultAsync(
        string rawXml, PayeeVerificationResponseBuilder.Request report, CBVerificationResultDto result, CancellationToken ct)
    {
        var sourceMessageId = Required(report.BizMsgIdr, "acmt.024 AppHdr BizMsgIdr");
        var now = Now;
        await using var transaction = await db.BeginTransactionAsync(ct);
        // Lock the correlated operation so two different results for the same operation cannot both complete it.
        var operation = await db.PapssOperations
            .FromSqlInterpolated($@"SELECT * FROM papss_operations
                WHERE direction = 'OUTBOUND' AND operation = 'VERIFICATION'
                  AND (requestmessageid = {result.RequestMessageId} OR verificationid = {result.VerificationId})
                ORDER BY CASE WHEN requestmessageid = {result.RequestMessageId} THEN 0 ELSE 1 END, createdat
                LIMIT 1 FOR UPDATE")
            .ToListAsync(ct) is { Count: > 0 } locked ? locked[0] : null;

        var firstResult = operation is null || operation.CompletedAt is null;
        var @event = new PapssOperationEvent
        {
            OperationId = operation?.Id,
            EventType = PapssEventTypes.VerificationResult,
            MessageType = Acmt024,
            SourceMessageId = sourceMessageId,
            RawXml = Encoding.UTF8.GetBytes(rawXml),
            ReceivedAt = now,
            // Unmatched results are still pushed: the result is self-describing (requestMessageId,
            // verificationId) and this preserves delivery for verifications submitted before the
            // operation store existed. Only a second result for an already-completed operation is not pushed.
            PushState = firstResult ? PapssDeliveryState.Pending : PapssDeliveryState.NotRequired,
            PushNextAttemptAt = firstResult ? now : null
        };
        db.PapssOperationEvents.Add(@event);
        if (operation is not null && firstResult)
        {
            operation.PapssOutcome = result.Verified ? PapssOutcome.VerifiedMatch : PapssOutcome.VerifiedNoMatch;
            operation.Verified = result.Verified;
            operation.AccountName = result.AccountName;
            operation.Currency = result.Currency;
            operation.Reason = result.Reason;
            operation.AdditionalInfo = result.AdditionalInfo;
            operation.SignedResponse = @event.RawXml;
            operation.BankDeliveryState = PapssDeliveryState.Pending;
            operation.CompletedAt = now;
            operation.UpdatedAt = now;
            // A result proves PAPSS admitted the request even if our submission outcome was ambiguous.
            if (operation.GatewayState is PapssGatewayState.Submitting or PapssGatewayState.SubmissionUnknown)
                operation.GatewayState = PapssGatewayState.Admitted;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            logger.LogInformation("PAPSS verification result {SourceMessageId} was already stored; not pushing it again", sourceMessageId);
            return VerificationResultInboxOutcome.Duplicate;
        }

        if (operation is null)
            logger.LogWarning("PAPSS verification result {SourceMessageId} (RequestMessageId={RequestMessageId}, VerificationId={VerificationId}) matches no stored operation; stored uncorrelated and queued for the bank",
                sourceMessageId, result.RequestMessageId, result.VerificationId);
        else if (!firstResult)
            logger.LogWarning("PAPSS verification result {SourceMessageId} arrived for already-completed operation {RequestMessageId}; stored for audit, not pushed", sourceMessageId, operation.RequestMessageId);
        return VerificationResultInboxOutcome.Stored;
    }

    // ----------------------------------------------------------------------------------------
    // Inbound verification enquiry (foreign participant -> PAPSS -> SIPS Connect -> core bank)
    // ----------------------------------------------------------------------------------------

    /// <summary>Stores the inbound acmt.023 keyed by the PAPSS source message id. Created=false for a redelivery.</summary>
    public async Task<(PapssOperation Operation, bool Created)> CreateInboundEnquiryAsync(
        PayeeVerificationBuilder.Request request, string sourceMessageId, string rawXml, CancellationToken ct)
    {
        var now = Now;
        var sourceCreated = ToUtc(request.CreDt);
        var operation = new PapssOperation
        {
            Direction = PapssDirection.Inbound,
            Operation = PapssOperationType.VerificationEnquiry,
            RequestMessageId = sourceMessageId,
            VerificationId = request.SIPSRequestId,
            CounterpartyBic = request.From,
            AccountId = request.Alias,
            AccountType = request.Type,
            GatewayState = PapssGatewayState.NotSubmitted,
            PapssOutcome = PapssOutcome.Pending,
            BankDeliveryState = PapssDeliveryState.Pending,
            SignedRequest = Encoding.UTF8.GetBytes(rawXml),
            SourceCreatedAt = sourceCreated,
            ReceivedAt = now,
            DeadlineAt = InboundDeadline(sourceCreated, now),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PapssOperations.Add(operation);
        db.PapssOperationEvents.Add(new PapssOperationEvent
        {
            Operation = operation,
            EventType = PapssEventTypes.VerificationEnquiry,
            MessageType = Acmt023,
            SourceMessageId = sourceMessageId,
            RawXml = operation.SignedRequest,
            ReceivedAt = now,
            PushState = PapssDeliveryState.NotRequired
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
                .SingleAsync(x => x.Direction == PapssDirection.Inbound && x.RequestMessageId == sourceMessageId, ct);
            return (existing, false);
        }
    }

    /// <summary>Record-only deadline: computed only when both the value and the clock are configured.</summary>
    public DateTimeOffset? InboundDeadline(DateTimeOffset? sourceCreatedAt, DateTimeOffset receivedAt)
    {
        var settings = options.Inbound.Acmt023;
        if (settings.ResponseDeadlineSeconds is not { } seconds || settings.DeadlineClock is not { } source) return null;
        var start = source == PapssDeadlineClock.SourceCreationTime ? sourceCreatedAt : receivedAt;
        return start?.AddSeconds(seconds);
    }

    /// <summary>The core bank did not answer: no acmt.024 is fabricated (PAPSS negative-reply rules are unresolved).</summary>
    public Task RecordInboundCoreBankFailureAsync(Guid id, string reasonCode, string? detail, CancellationToken ct)
        => db.PapssOperations.Where(x => x.Id == id && x.BankDeliveryState == PapssDeliveryState.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.BankDeliveryState, PapssDeliveryState.Failed)
                .SetProperty(x => x.PapssOutcome, PapssOutcome.Unknown)
                .SetProperty(x => x.ReasonCode, reasonCode)
                .SetProperty(x => x.AdditionalInfo, detail)
                .SetProperty(x => x.UpdatedAt, Now), ct);

    /// <summary>Stores the core-bank answer and queues the signed reply in one transaction.</summary>
    public async Task<PapssOutboundResponse> RecordInboundAnswerAndQueueReplyAsync(
        Guid id, PayeeVerificationResponseBuilder.Request answer, string bankReason, string replyBizMsgIdr, string signedReply, CancellationToken ct)
    {
        var now = Now;
        var bytes = Encoding.UTF8.GetBytes(signedReply);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var operation = await db.PapssOperations.SingleAsync(x => x.Id == id, ct);
        operation.PapssOutcome = answer.Verified ? PapssOutcome.VerifiedMatch : PapssOutcome.VerifiedNoMatch;
        operation.Verified = answer.Verified;
        operation.AccountName = NullIfEmpty(answer.Name);
        operation.Currency = NullIfEmpty(answer.Currency);
        operation.Reason = NullIfEmpty(bankReason);
        operation.AdditionalInfo = NullIfEmpty(answer.AdditionalInfo);
        operation.BankDeliveryState = PapssDeliveryState.Delivered;
        operation.GatewayState = PapssGatewayState.Submitting;
        operation.SignedResponse = bytes;
        operation.UpdatedAt = now;
        var reply = new PapssOutboundResponse
        {
            OperationId = id,
            BizMsgIdr = replyBizMsgIdr,
            MessageType = Acmt024,
            SignedXml = bytes,
            State = PapssResponseState.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PapssOutboundResponses.Add(reply);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return reply;
    }

    /// <summary>
    /// For a redelivered enquiry: keep the existing reply (same bytes, same answer). Only a reply whose
    /// retries were exhausted is re-queued; pending/admitted/rejected/held replies are left as they are.
    /// </summary>
    public async Task<PapssOutboundResponse?> EnsureReplyQueuedAsync(Guid operationId, CancellationToken ct)
    {
        var now = Now;
        await db.PapssOutboundResponses.Where(x => x.OperationId == operationId && x.State == PapssResponseState.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, PapssResponseState.Pending)
                .SetProperty(x => x.Attempts, 0)
                .SetProperty(x => x.NextAttemptAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return await db.PapssOutboundResponses.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, ct);
    }

    // ----------------------------------------------------------------------------------------
    // Lookup
    // ----------------------------------------------------------------------------------------

    public async Task<PapssOperation?> FindAsync(string requestMessageId, bool outboundVerificationOnly, CancellationToken ct)
    {
        var query = db.PapssOperations.AsNoTracking().Where(x => x.RequestMessageId == requestMessageId);
        if (outboundVerificationOnly)
            query = query.Where(x => x.Direction == PapssDirection.Outbound && x.Operation == PapssOperationType.Verification);
        // OUTBOUND first: that is the id this participant's bank was given.
        var rows = await query.ToListAsync(ct);
        return rows.OrderBy(x => x.Direction == PapssDirection.Outbound ? 0 : 1).FirstOrDefault();
    }

    public Task<PapssOutboundResponse?> FindReplyAsync(Guid operationId, CancellationToken ct)
        => db.PapssOutboundResponses.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, ct);

    // ----------------------------------------------------------------------------------------

    public static bool IsUniqueViolation(DbUpdateException error)
        => error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static DateTimeOffset? ToUtc(DateTime value)
    {
        if (value == default) return null;
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"The PAPSS message is missing {name}.");
}
