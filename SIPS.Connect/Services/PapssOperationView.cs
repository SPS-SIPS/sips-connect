using System.Globalization;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Outcome = SIPS.PostgreSQL.Enums.PapssOutcome;

namespace SIPS.Connect.Services;

/// <summary>Bank-facing view of a stored PAPSS operation (jsonAdapter mapping <c>OperationResult</c>).</summary>
public sealed class PapssOperationResult
{
    public string RequestMessageId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    /// <summary>Single summary: PENDING, COMPLETED, REJECTED, FAILED, UNKNOWN or EXPIRED.</summary>
    public string Status { get; set; } = string.Empty;
    public string GatewayState { get; set; } = string.Empty;
    public string PapssOutcome { get; set; } = string.Empty;
    public string BankDeliveryState { get; set; } = string.Empty;
    public string? VerificationId { get; set; }
    public string? CounterpartyBic { get; set; }
    public bool? Verified { get; set; }
    public string? AccountName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountType { get; set; }
    public string? Currency { get; set; }
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public string? AdmissionCode { get; set; }
    public string? ReasonCode { get; set; }
    public string? ReplyState { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public string? DeadlineAt { get; set; }
    public long AgeSeconds { get; set; }

    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
    public const string Rejected = "REJECTED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
    public const string Expired = "EXPIRED";

    public static PapssOperationResult From(PapssOperation op, PapssOutboundResponse? reply, DateTimeOffset now, int? outboundExpirySeconds) => new()
    {
        RequestMessageId = op.RequestMessageId,
        Operation = UpperSnakeEnumConverter<PapssOperationType>.Of(op.Operation),
        Direction = UpperSnakeEnumConverter<PapssDirection>.Of(op.Direction),
        Status = Summarize(op, reply, now, outboundExpirySeconds),
        GatewayState = UpperSnakeEnumConverter<PapssGatewayState>.Of(op.GatewayState),
        PapssOutcome = UpperSnakeEnumConverter<Outcome>.Of(op.PapssOutcome),
        BankDeliveryState = UpperSnakeEnumConverter<PapssDeliveryState>.Of(op.BankDeliveryState),
        VerificationId = op.VerificationId,
        CounterpartyBic = op.CounterpartyBic,
        Verified = op.Verified,
        AccountName = op.AccountName,
        AccountNumber = op.AccountId,
        AccountType = op.AccountType,
        Currency = op.Currency,
        Reason = op.Reason,
        AdditionalInfo = op.AdditionalInfo,
        AdmissionCode = op.AdmissionCode,
        ReasonCode = op.ReasonCode,
        ReplyState = reply is null ? null : UpperSnakeEnumConverter<PapssResponseState>.Of(reply.State),
        CreatedAt = Iso(op.CreatedAt)!,
        CompletedAt = Iso(op.CompletedAt),
        DeadlineAt = Iso(op.DeadlineAt),
        AgeSeconds = Math.Max(0, (long)(now - op.CreatedAt).TotalSeconds)
    };

    /// <summary>Derives one summary status from the three independent state dimensions.</summary>
    public static string Summarize(PapssOperation op, PapssOutboundResponse? reply, DateTimeOffset now, int? outboundExpirySeconds)
    {
        if (op.Direction == PapssDirection.Outbound)
        {
            if (op.GatewayState == PapssGatewayState.Rejected || op.PapssOutcome == Outcome.Rejected) return Rejected;
            if (op.PapssOutcome is Outcome.VerifiedMatch or Outcome.VerifiedNoMatch) return Completed;
            if (op.PapssOutcome == Outcome.Unknown) return Unknown;
            // Record-only expiry: nothing is sent to PAPSS; a late result still completes the operation.
            if (outboundExpirySeconds is { } expiry && now - op.CreatedAt > TimeSpan.FromSeconds(expiry)) return Expired;
            if (op.GatewayState == PapssGatewayState.SubmissionUnknown) return Unknown;
            return Pending;
        }

        if (op.BankDeliveryState == PapssDeliveryState.Failed) return Failed;
        if (op.GatewayState == PapssGatewayState.Rejected || reply?.State == PapssResponseState.Rejected) return Rejected;
        if (op.GatewayState == PapssGatewayState.Admitted) return Completed;
        if (reply?.State == PapssResponseState.Held) return Failed;
        if (reply?.State == PapssResponseState.Failed) return Unknown;
        return Pending;
    }

    private static string? Iso(DateTimeOffset? value) => value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
