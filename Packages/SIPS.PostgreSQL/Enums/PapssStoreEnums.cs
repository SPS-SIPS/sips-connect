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
    /// <summary>pacs.008: OUTBOUND = bank-initiated credit transfer; INBOUND = credit transfer received from PAPSS.</summary>
    Payment,
    /// <summary>pacs.028 sent for a stored (outbound) payment; linked to it by OriginalOperationId.</summary>
    StatusEnquiry,
    /// <summary>pacs.004: OUTBOUND = return this participant sends; INBOUND = return received from PAPSS.</summary>
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
/// Business outcome. Verification: OUTBOUND = the result PAPSS returned, INBOUND = the answer this participant gave.
/// Payment / return: the payment outcome derived from the pacs.002 sequence (the raw ISO status is kept separately
/// in papss_operations.paymentstatus and per event). ACCEPTED covers ACCP and ACSP; SETTLED is ACSC; REJECTED is RJCT;
/// RETURNED marks a payment against which a return has settled. SETTLED, REJECTED and RETURNED are final.
/// </summary>
public enum PapssOutcome
{
    Pending,
    VerifiedMatch,
    VerifiedNoMatch,
    Rejected,
    Unknown,
    Accepted,
    Settled,
    Returned
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
    /// <summary>pacs.002 (payment / return / status-enquiry result) received from the gateway.</summary>
    public const string PaymentStatus = "PAYMENT_STATUS";
    /// <summary>pacs.008 received from the gateway (inbound credit transfer).</summary>
    public const string PaymentReceived = "PAYMENT_RECEIVED";
    /// <summary>pacs.004 received from the gateway (a return of a payment this participant sent).</summary>
    public const string ReturnReceived = "RETURN_RECEIVED";
}

/// <summary>How a received pacs.002 / pacs.004 was matched to a stored operation (papss_operation_events.correlation).</summary>
public static class PapssCorrelation
{
    /// <summary>OrgnlMsgId (+ OrgnlMsgNmId) matched the stored operation's message id.</summary>
    public const string MessageId = "MSG_ID";
    /// <summary>Secondary key: OrgnlTxId + OrgnlEndToEndId matched exactly one stored operation.</summary>
    public const string TransactionId = "TX_ID";
    /// <summary>No stored operation matches.</summary>
    public const string None = "NONE";
    /// <summary>The OrgnlMsgId matched but OrgnlTxId/OrgnlEndToEndId did not: not attached.</summary>
    public const string Mismatch = "MISMATCH";
}

/// <summary>What a received status did to the operation (papss_operation_events.disposition).</summary>
public static class PapssEventDisposition
{
    public const string Applied = "APPLIED";
    /// <summary>Correlated but did not advance the state (e.g. ACCP after ACSP); kept as history, not pushed.</summary>
    public const string NotAdvancing = "NOT_ADVANCING";
    /// <summary>The same final status again under a different source message id; not pushed again.</summary>
    public const string DuplicateFinal = "DUPLICATE_FINAL";
    /// <summary>A different final status after a final one: flagged on the operation, never applied.</summary>
    public const string Conflict = "CONFLICT";
    /// <summary>Status code outside ACCP/ACSP/ACSC/PDNG/RJCT: kept, not applied.</summary>
    public const string UnknownStatus = "UNKNOWN_STATUS";
    public const string Uncorrelated = "UNCORRELATED";
}
