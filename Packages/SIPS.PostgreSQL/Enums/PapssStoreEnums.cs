namespace SIPS.PostgreSQL.Enums;

// PAPSS operation store state dimensions. They are persisted as UPPER_SNAKE_CASE strings
// (see UpperSnakeEnumConverter) so operators can read them directly in SQL.

public enum PapssDirection
{
    Outbound,
    Inbound
}

public enum PapssOperationType
{
    /// <summary>Bank-initiated acmt.023 sent to PAPSS; the result arrives later as acmt.024.</summary>
    Verification,
    /// <summary>acmt.023 received from PAPSS (a foreign participant verifies one of our accounts).</summary>
    VerificationEnquiry,
    // Reserved for later phases; not produced yet.
    Payment,
    StatusEnquiry,
    Return
}

/// <summary>
/// Technical admission state of the message this participant submits to the WP-SIPS gateway:
/// the outbound request (OUTBOUND) or the acmt.024 reply (INBOUND).
/// </summary>
public enum PapssGatewayState
{
    NotSubmitted,
    Submitting,
    Admitted,
    Rejected,
    SubmissionUnknown
}

/// <summary>
/// Business outcome. OUTBOUND: the result PAPSS returned. INBOUND: the answer this participant gave.
/// </summary>
public enum PapssOutcome
{
    Pending,
    VerifiedMatch,
    VerifiedNoMatch,
    Rejected,
    Unknown
}

/// <summary>
/// OUTBOUND: delivery of the result to the bank callback. INBOUND: whether the core bank answered
/// the enquiry. Also used for the per-event bank push outbox.
/// </summary>
public enum PapssDeliveryState
{
    NotRequired,
    Pending,
    Delivered,
    Failed
}

/// <summary>State of a gateway-bound reply in the response outbox.</summary>
public enum PapssResponseState
{
    Pending,
    Admitted,
    Rejected,
    /// <summary>Retries exhausted without an admission; needs operator attention.</summary>
    Failed,
    /// <summary>Not submitted because it is past its deadline and LateResponsePolicy=Hold.</summary>
    Held
}

public static class PapssEventTypes
{
    public const string VerificationResult = "VERIFICATION_RESULT";
    public const string VerificationEnquiry = "VERIFICATION_ENQUIRY";
}
