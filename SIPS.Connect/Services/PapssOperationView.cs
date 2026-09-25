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

    // ---- Payments / returns / status enquiries (null for verification operations) --------------
    public string? TxId { get; set; }
    public string? EndToEndId { get; set; }
    public string? MsgId { get; set; }
    public decimal? Amount { get; set; }
    public string? LocalInstrument { get; set; }
    /// <summary>Raw ISO status in effect (ACCP, ACSP, ACSC, PDNG, RJCT).</summary>
    public string? PaymentStatus { get; set; }
    /// <summary>PENDING, ACCEPTED, SETTLED, REJECTED, RETURNED or UNKNOWN.</summary>
    public string? PaymentOutcome { get; set; }
    public string? StatusReasonCode { get; set; }
    public string? StatusAt { get; set; }
    public bool? StatusConflict { get; set; }
    public string? ReturnId { get; set; }
    public string? OriginalTxId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    /// <summary>requestMessageId of the operation this one refers to (a return's payment, an enquiry's payment).</summary>
    public string? OriginalRequestMessageId { get; set; }
    /// <summary>Returns (and their outcome) recorded against this payment, e.g. "OUTBOUND RTN-1 SETTLED".</summary>
    public List<string>? Returns { get; set; }
    /// <summary>INBOUND payment: state of the PAPSS decision (pacs.002 ACCP/RJCT) in the decision outbox.</summary>
    public string? DecisionState { get; set; }
    public List<PapssStatusHistoryEntry>? StatusHistory { get; set; }

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

    public static bool IsPaymentLike(PapssOperation op) => op.Operation is PapssOperationType.Payment or PapssOperationType.Return or PapssOperationType.StatusEnquiry;

    /// <summary>Adds the payment fields and the received-status history (payments, returns, status enquiries).</summary>
    public PapssOperationResult WithPayment(PapssOperation op, IEnumerable<PapssOperationEvent> history, PapssOperation? original, IEnumerable<PapssOperation> linked)
    {
        TxId = op.TxId;
        EndToEndId = op.EndToEndId;
        MsgId = op.MsgId;
        Amount = op.Amount;
        LocalInstrument = op.LocalInstrument;
        PaymentStatus = op.PaymentStatus;
        PaymentOutcome = UpperSnakeEnumConverter<Outcome>.Of(op.PapssOutcome);
        StatusReasonCode = op.StatusReasonCode;
        StatusAt = Iso(op.StatusAt);
        StatusConflict = op.StatusConflict;
        ReturnId = op.ReturnId;
        OriginalTxId = op.OriginalTxId;
        OriginalEndToEndId = op.OriginalEndToEndId;
        OriginalRequestMessageId = original?.RequestMessageId;
        var returns = linked.Where(x => x.Operation == PapssOperationType.Return)
            .Select(x => $"{UpperSnakeEnumConverter<PapssDirection>.Of(x.Direction)} {x.ReturnId} {UpperSnakeEnumConverter<Outcome>.Of(x.PapssOutcome)}").ToList();
        Returns = returns.Count == 0 ? null : returns;
        if (op.Direction == PapssDirection.Inbound && op.Operation == PapssOperationType.Payment)
            DecisionState = op.GatewayState switch
            {
                PapssGatewayState.NotSubmitted => "NOT_QUEUED",
                PapssGatewayState.Submitting or PapssGatewayState.SubmissionUnknown => "PENDING",
                PapssGatewayState.Admitted => "PUBLISHED",
                _ => "FAILED"
            };
        StatusHistory = history.Select(PapssStatusHistoryEntry.From).ToList();
        return this;
    }

    /// <summary>Derives one summary status from the three independent state dimensions.</summary>
    public static string Summarize(PapssOperation op, PapssOutboundResponse? reply, DateTimeOffset now, int? outboundExpirySeconds)
    {
        if (IsPaymentLike(op))
        {
            if (op.PapssOutcome is Outcome.Settled or Outcome.Returned) return Completed;
            if (op.PapssOutcome == Outcome.Rejected || op.Direction == PapssDirection.Outbound && op.GatewayState == PapssGatewayState.Rejected) return Rejected;
            if (op.Operation == PapssOperationType.StatusEnquiry && op.CompletedAt is not null) return Completed;
            if (op.PapssOutcome == Outcome.Unknown) return Unknown;
            if (op.Direction == PapssDirection.Outbound && op.GatewayState == PapssGatewayState.SubmissionUnknown && op.PapssOutcome == Outcome.Pending) return Unknown;
            if (op.Direction == PapssDirection.Inbound && op.BankDeliveryState == PapssDeliveryState.Failed) return Failed;
            return Pending;
        }

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

    internal static string? Iso(DateTimeOffset? value) => value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>One received pacs.002 / pacs.004 / pacs.008 of an operation (jsonAdapter OperationResult.statusHistory).</summary>
public sealed class PapssStatusHistoryEntry
{
    public string ReceivedAt { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string SourceMessageId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? ReasonCode { get; set; }
    public string? Correlation { get; set; }
    public string? Disposition { get; set; }
    public string PushState { get; set; } = string.Empty;
    public string? Note { get; set; }
    // Evidence layers: (1) raw PAPSS message = sourceMessageId + rawEvidenceReference (SHA-256 of the signed PAPSS bytes kept by
    // the gateway); (2) the normalized fields above and amount/currency; (3) local enrichment = amountSource,
    // categoryPurposeSource and fieldProvenance (path -> NETWORK_REPORTED / LOCAL_RECONSTRUCTION / IDENTIFIER_TRANSLATION /
    // DEFAULT_FILLER). A LOCAL_RECONSTRUCTION amount was not reported by PAPSS.
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? AmountSource { get; set; }
    public string? CategoryPurposeSource { get; set; }
    public string? RawEvidenceReference { get; set; }
    public Dictionary<string, string>? FieldProvenance { get; set; }

    public static PapssStatusHistoryEntry From(PapssOperationEvent e) => new()
    {
        Amount = e.Amount,
        Currency = e.Currency,
        AmountSource = e.AmountSource,
        CategoryPurposeSource = e.CategoryPurposeSource,
        RawEvidenceReference = e.RawEvidenceReference,
        FieldProvenance = e.FieldProvenance is { Length: > 0 } json ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) : null,
        ReceivedAt = PapssOperationResult.Iso(e.ReceivedAt)!,
        MessageType = e.MessageType,
        SourceMessageId = e.SourceMessageId,
        Status = e.Status,
        ReasonCode = e.ReasonCode,
        Correlation = e.Correlation,
        Disposition = e.Disposition,
        PushState = UpperSnakeEnumConverter<PapssDeliveryState>.Of(e.PushState),
        Note = e.Note
    };
}
