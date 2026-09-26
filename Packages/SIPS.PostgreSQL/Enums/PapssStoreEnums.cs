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
    Return,
    /// <summary>
    /// camt.056 (OUTBOUND): the bank recalls one of its settled payments; linked to it by OriginalOperationId. The recall id is
    /// the RequestMessageId. PAPSS answers with pacs.002 (ACCP/RJCT), the beneficiary later with pacs.004 or camt.029 (RJCR).
    /// </summary>
    Recall
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
/// Recall (camt.056): RECALL_PENDING (submitted, no PAPSS answer yet), RECALL_ACCEPTED_BY_PAPSS (pacs.002 ACCP: accepted
/// for processing, NOT completed) and RECALL_OUTCOME_UNRESOLVED (the gateway could not determine PAPSS's outcome for the
/// recall; reported on the same recall-result callback/lookup path as the other answers) are open; RECALL_REJECTED_BY_PAPSS
/// (pacs.002 RJCT), RECALL_REJECTED_BY_BENEFICIARY (camt.029 RJCR), RECALL_RETURNED (pacs.004 received) and RECALL_ABANDONED
/// (a manual operator close of a recall stuck open, see <c>POST Recall/{recallId}/Close</c>) are final. A recall the gateway
/// rejected before submission is REJECTED.
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
    Returned,
    RecallPending,
    RecallAcceptedByPapss,
    RecallRejectedByPapss,
    RecallRejectedByBeneficiary,
    RecallReturned,
    /// <summary>
    /// The gateway could not read a definite ACCP/RJCT outcome from PAPSS for the recall (ambiguous/unreadable). Still OPEN:
    /// blocks a new recall on the same payment (ux_papss_op_open_recall) until an operator closes it or a later, legitimate
    /// answer (camt.029 / pacs.004) resolves it normally.
    /// </summary>
    RecallOutcomeUnresolved,
    /// <summary>
    /// An operator manually closed a recall stuck open (e.g. RECALL_OUTCOME_UNRESOLVED). Final; releases the one-open-recall
    /// lock so a new recall may be submitted. Never applied automatically.
    /// </summary>
    RecallAbandoned
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
    /// <summary>pacs.002 answering our camt.056 (OrgnlMsgNmId camt.056.*): PAPSS accepted (ACCP) or rejected (RJCT) the recall.</summary>
    public const string RecallStatus = "RECALL_STATUS";
    /// <summary>camt.029 received: the beneficiary refused our recall (RJCR).</summary>
    public const string RecallResolution = "RECALL_RESOLUTION";
    /// <summary>pacs.004 received while a recall was open on the payment: the recall is RETURNED (the return itself is RETURN_RECEIVED).</summary>
    public const string RecallReturned = "RECALL_RETURNED";
    /// <summary>An operator manually closed a recall stuck open (e.g. RECALL_OUTCOME_UNRESOLVED). Audit record, never applied automatically.</summary>
    public const string RecallClosed = "RECALL_CLOSED";
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
