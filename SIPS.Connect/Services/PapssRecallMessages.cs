using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SIPS.PostgreSQL.Enums;
using M = SIPS.Connect.Services.PapssPaymentMessages;

namespace SIPS.Connect.Services;

/// <summary>Bank request for <c>POST /api/v1/Gateway/Recall</c> (jsonAdapter mapping <c>RecallRequest</c>).</summary>
public sealed class PapssRecallRequest
{
    /// <summary>Optional; a recall is PAPSS-only, so anything but PAPSS is refused.</summary>
    public string? Rail { get; set; }
    /// <summary>The bank's TxId of the OUTBOUND payment to recall (this or <see cref="EndToEndId"/> is required).</summary>
    public string? TxId { get; set; }
    public string? EndToEndId { get; set; }
    /// <summary>Recall reason (camt.056 CxlRsnInf/Rsn/Cd). ISO shape only (1-4 letters/digits); PAPSS publishes no authoritative list.</summary>
    public string? Reason { get; set; }
    /// <summary>Optional idempotency key, in the contract shape <c>SIPS-</c> + 24 lowercase hex. Generated when absent.</summary>
    public string? RecallId { get; set; }
}

/// <summary>What SIPS Connect puts into the camt.056.001.08 it sends for a recall.</summary>
public sealed record PapssRecallInstruction(
    string RecallId,
    string BankBic,
    string RemoteWpSipsIdentity,
    string OriginalMessageId,
    string OriginalEndToEndId,
    string OriginalTxId,
    decimal Amount,
    string Currency,
    string Reason,
    DateTimeOffset CreatedAt);

/// <summary>
/// R2: camt.056.001.09 delivered by the gateway -- a counterparty has recalled a payment this institution RECEIVED. The
/// gateway's own notification contract (deliberately NOT camt.056.001.08, which is R1's SIPS-to-Gateway recall REQUEST in
/// the opposite direction). OriginalTxId/OriginalEndToEndId are THIS institution's own identifiers for the received payment
/// (resolved by the gateway from its stored admission, not PAPSS's OrgnlMsgId, which names the recalling bank's own wire id).
/// </summary>
public sealed record PapssInboundRecallMessage(
    string SourceMessageId,
    DateTimeOffset? SourceCreatedAt,
    string CancellationId,
    string OriginalTxId,
    string OriginalEndToEndId,
    string? ReasonCode,
    string? AdditionalInfo,
    PapssProvenance? Provenance = null);

/// <summary>camt.029.001.09 delivered by the gateway: the beneficiary's negative answer (RJCR) to our recall.</summary>
public sealed record PapssRecallResolution(
    string SourceMessageId,
    DateTimeOffset? SourceCreatedAt,
    string? AssignmentId,
    string? ResolvedCaseId, // RslvdCase/Id = our recall id; absent when the gateway could not prove an open recall
    string? Confirmation,
    string? ResponderId, // CxlStsId: the responder's own id for this answer
    string? OriginalMessageId,
    string? OriginalMessageType,
    string OriginalTxId,
    string? OriginalEndToEndId,
    string? ReasonCode,
    string? AdditionalInfo,
    PapssProvenance? Provenance = null);

/// <summary>Body of the bank push for a recall answer (jsonAdapter mapping <c>{profile}.CB_RecallResult</c>).</summary>
public sealed class PapssRecallResultPush
{
    public string RecallId { get; set; } = string.Empty;
    public string? TxId { get; set; }
    public string? EndToEndId { get; set; }
    /// <summary>RECALL_ACCEPTED_BY_PAPSS, RECALL_REJECTED_BY_PAPSS, RECALL_REJECTED_BY_BENEFICIARY, RECALL_RETURNED, RECALL_OUTCOME_UNRESOLVED or RECALL_ABANDONED.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? ReasonCode { get; set; }
    /// <summary>The answering party's id: pacs.002 StsId, camt.029 CxlStsId or pacs.004 RtrId.</summary>
    public string? ResponderId { get; set; }
    public string SourceMessageId { get; set; } = string.Empty;
    public string ReceivedAt { get; set; } = string.Empty;
}

/// <summary>Operator request for <c>POST /api/v1/Gateway/Recall/{recallId}/Close</c> (a manual, audited close of a recall stuck open).</summary>
public sealed class PapssRecallCloseRequest
{
    /// <summary>Required: the operator's reason for closing the recall manually (e.g. why the gateway's outcome could not be trusted/resolved).</summary>
    public string? Reason { get; set; }
}

/// <summary>R2: bank request for <c>POST /api/v1/Gateway/Recall/Inbound/{recallId}/Decision</c>.</summary>
public sealed class PapssInboundRecallDecisionRequest
{
    /// <summary>Required: ACCEPT or REJECT (case-insensitive).</summary>
    public string? Decision { get; set; }
    /// <summary>Required for REJECT (camt.029 CxlStsRsnInf/Rsn/Cd); optional for ACCEPT (the pacs.004 return reason -- defaults to the recall's own stored reason).</summary>
    public string? Reason { get; set; }
}

/// <summary>R2: result of deciding an inbound recall.</summary>
public sealed record PapssInboundRecallDecisionResponse(string RecallId, string Decision, PapssAdmissionResponse? Admission, string? ReturnId);

/// <summary>Which of the two authorized paths closed a recall: recorded in the RECALL_CLOSED audit event for every call.</summary>
public static class PapssRecallCloseAuthPath
{
    /// <summary>A human operator, authenticated via Keycloak/JWT with the Recon role (the same path as Retry).</summary>
    public const string Operator = "OPERATOR";
    /// <summary>An API party, authenticated via API key with the narrow KnownRoles.RecallClose capability.</summary>
    public const string ApiParty = "API_PARTY";
}

/// <summary>Result of <see cref="PapssOperationStore.CloseRecallAsync"/>.</summary>
public enum PapssRecallCloseOutcome
{
    /// <summary>No stored recall has this recallId.</summary>
    NotFound,
    /// <summary>The recall is not OPEN (already final): nothing was written.</summary>
    AlreadyClosed,
    /// <summary>Closed to RECALL_ABANDONED; the one-open-recall lock on the payment is released.</summary>
    Closed
}

/// <summary>
/// Builds the outbound camt.056.001.08 recall request (there are no camt builders or schemas in SIPS.ISO20022) and reads the
/// camt.029.001.09 answer, by exact element path like <see cref="PapssPaymentMessages"/>.
/// </summary>
public static partial class PapssRecallMessages
{
    public const string Camt056 = "camt.056.001.08";
    public const string Camt029 = "camt.029.001.09";
    /// <summary>R2: the gateway's own notification contract for an inbound recall (see <see cref="PapssInboundRecallMessage"/>). Deliberately a different version from <see cref="Camt056"/>.</summary>
    public const string InboundRecallDefinition = "camt.056.001.09";
    /// <summary>R2: this institution's own camt.029 rejecting an inbound recall (see <see cref="PapssInboundRecallRejection"/>). Deliberately a different version from <see cref="Camt029"/> (the gateway's resolution-delivery contract for R1).</summary>
    public const string InboundRecallRejectionDefinition = "camt.029.001.08";
    public const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    public const string Camt056Namespace = "urn:iso:std:iso:20022:tech:xsd:" + Camt056;
    /// <summary>Root of the SIPS envelope. The gateway reads AppHdr and Document under the root and does not check the root name.</summary>
    public const string EnvelopeNamespace = "urn:iso:std:iso:20022:tech:xsd:paymentRecall_request";
    public const string RejectedConfirmation = "RJCR";
    /// <summary>
    /// The recall-result signal the gateway uses (S3) when it could not read a definite ACCP/RJCT outcome from PAPSS for a
    /// camt.056 answer. Gateway-confirmed 2026-09-26 wire shape: a real, gateway-synthesized pacs.002.001.12 (correlated the
    /// same as any other recall answer: OrgnlMsgId = recall id, OrgnlMsgNmId = camt.056.001.08, delivered idempotently
    /// through the normal signed/durable pipeline) with TxSts=PDNG and <c>StsRsnInf/Rsn/Prtry</c> (not Cd) equal to this
    /// literal. Read the same way as any other reason code (<see cref="PapssPaymentMessages.ParseStatusReport"/>, which falls
    /// back from Cd to Prtry) and recognized regardless of TxSts: pacs.002 TxSts is an ISO ExternalPaymentTransactionStatus1Code,
    /// hard-capped at 4 characters (see PaymentResponse.cs ExternalPaymentTransactionStatus1Code.TypeDefinition), so this
    /// literal could never itself be the TxSts value.
    /// </summary>
    public const string UnresolvedStatus = "RECALL_OUTCOME_UNRESOLVED";

    /// <summary>OrgnlMsgNmId of a pacs.002 that answers our camt.056 (any camt.056 version): never a payment status.</summary>
    public static bool IsRecallAnswer(string? originalMessageType)
        => originalMessageType is not null && originalMessageType.StartsWith("camt.056", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^SIPS-[0-9a-f]{24}$", RegexOptions.CultureInvariant)]
    private static partial Regex RecallIdShape();

    public static bool IsRecallId(string? value) => value is not null && RecallIdShape().IsMatch(value);

    /// <summary>ISO ExternalCancellationReason1Code shape (1-4 letters/digits), upper-cased. No value list is enforced (sources contradict).</summary>
    public static string NormalizeReason(string? reason)
    {
        var code = reason?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) throw new ArgumentException("A recall reason is required.");
        if (code.Length > 4 || !code.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException("The recall reason must be a 1-4 character ISO cancellation reason code.");
        return code;
    }

    /// <summary>
    /// The unsigned SIPS envelope: AppHdr (Fr = bank BIC, To = remote WP-SIPS identity, BizMsgIdr = recall id, MsgDefIdr
    /// camt.056.001.08; BizSvc is applied when signing) and FIToFIPmtCxlReq with Assgnmt/Id = CxlId = the recall id.
    /// </summary>
    public static string BuildRecallRequest(PapssRecallInstruction x)
    {
        XNamespace h = HeaderNamespace, d = Camt056Namespace, e = EnvelopeNamespace;
        var created = x.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        XElement Party(string name, string id) => new(h + name, new XElement(h + "FIId", new XElement(h + "FinInstnId", new XElement(h + "Othr", new XElement(h + "Id", id)))));
        var envelope = new XElement(e + "FPEnvelope",
            new XAttribute(XNamespace.Xmlns + "header", HeaderNamespace),
            new XAttribute(XNamespace.Xmlns + "document", Camt056Namespace),
            new XAttribute("Id", "BL-" + x.RecallId),
            new XElement(h + "AppHdr",
                Party("Fr", x.BankBic),
                Party("To", x.RemoteWpSipsIdentity),
                new XElement(h + "BizMsgIdr", x.RecallId),
                new XElement(h + "MsgDefIdr", Camt056),
                new XElement(h + "CreDt", created)),
            new XElement(d + "Document",
                new XElement(d + "FIToFIPmtCxlReq",
                    new XElement(d + "Assgnmt",
                        new XElement(d + "Id", x.RecallId),
                        new XElement(d + "Assgnr", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "BICFI", x.BankBic)))),
                        new XElement(d + "Assgne", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "Othr", new XElement(d + "Id", x.RemoteWpSipsIdentity))))),
                        new XElement(d + "CreDtTm", created)),
                    new XElement(d + "Undrlyg",
                        new XElement(d + "TxInf",
                            new XElement(d + "CxlId", x.RecallId),
                            new XElement(d + "OrgnlGrpInf",
                                new XElement(d + "OrgnlMsgId", x.OriginalMessageId),
                                new XElement(d + "OrgnlMsgNmId", PapssPaymentMessages.Pacs008)),
                            new XElement(d + "OrgnlEndToEndId", x.OriginalEndToEndId),
                            new XElement(d + "OrgnlTxId", x.OriginalTxId),
                            new XElement(d + "OrgnlIntrBkSttlmAmt", new XAttribute("Ccy", x.Currency), x.Amount.ToString("0.00###", CultureInfo.InvariantCulture)),
                            new XElement(d + "CxlRsnInf", new XElement(d + "Rsn", new XElement(d + "Cd", x.Reason))))))));
        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>R2: what SIPS Connect puts into the camt.029.001.08 it sends to reject an inbound recall (a counterparty's camt.056
    /// naming a payment this institution received). OriginalSenderBic is the authoritative counterparty BIC this institution
    /// already has stored against the received payment (never asked of PAPSS again); the gateway re-resolves the payment from it
    /// plus OriginalTxId/OriginalEndToEndId, the same way it would for a spontaneous return of a received payment.</summary>
    public sealed record PapssInboundRecallRejection(string RejectionId, string BankBic, string RemoteWpSipsIdentity, string OriginalTxId, string OriginalEndToEndId, string OriginalSenderBic, string ReasonCode, DateTimeOffset CreatedAt);

    /// <summary>
    /// R2: the unsigned SIPS envelope rejecting an inbound recall: AppHdr (Fr = bank BIC, To = remote WP-SIPS identity, BizMsgIdr =
    /// rejection id, MsgDefIdr camt.029.001.08 -- deliberately NOT .001.09, which is the gateway's own notification contract for the
    /// opposite, recall-resolution-delivery direction) and RsltnOfInvstgtn with Assgnmt/Id = CxlStsId = the rejection id, Sts/Conf
    /// RJCR, CxlDtls/TxInfAndSts{OrgnlEndToEndId, OrgnlTxId, CxlStsRsnInf/Rsn, OrgnlTxRef/DbtrAgt/FinInstnId/BICFI = the authoritative
    /// original sender}. There are no camt builders in SIPS.ISO20022, so this is hand-built like BuildRecallRequest.
    /// </summary>
    public static string BuildInboundRecallRejection(PapssInboundRecallRejection x)
    {
        XNamespace h = HeaderNamespace, d = "urn:iso:std:iso:20022:tech:xsd:" + InboundRecallRejectionDefinition, e = EnvelopeNamespace;
        var created = x.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        XElement Party(string name, string id) => new(h + name, new XElement(h + "FIId", new XElement(h + "FinInstnId", new XElement(h + "Othr", new XElement(h + "Id", id)))));
        var envelope = new XElement(e + "FPEnvelope",
            new XAttribute(XNamespace.Xmlns + "header", HeaderNamespace),
            new XAttribute(XNamespace.Xmlns + "document", d.NamespaceName),
            new XAttribute("Id", "BL-" + x.RejectionId),
            new XElement(h + "AppHdr",
                Party("Fr", x.BankBic),
                Party("To", x.RemoteWpSipsIdentity),
                new XElement(h + "BizMsgIdr", x.RejectionId),
                new XElement(h + "MsgDefIdr", InboundRecallRejectionDefinition),
                new XElement(h + "CreDt", created)),
            new XElement(d + "Document",
                new XElement(d + "RsltnOfInvstgtn",
                    new XElement(d + "Assgnmt",
                        new XElement(d + "Id", x.RejectionId),
                        new XElement(d + "Assgnr", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "BICFI", x.BankBic)))),
                        new XElement(d + "Assgne", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "Othr", new XElement(d + "Id", x.RemoteWpSipsIdentity))))),
                        new XElement(d + "CreDtTm", created)),
                    new XElement(d + "Sts", new XElement(d + "Conf", RejectedConfirmation)),
                    new XElement(d + "CxlDtls",
                        new XElement(d + "TxInfAndSts",
                            new XElement(d + "CxlStsId", x.RejectionId),
                            new XElement(d + "OrgnlEndToEndId", x.OriginalEndToEndId),
                            new XElement(d + "OrgnlTxId", x.OriginalTxId),
                            new XElement(d + "CxlStsRsnInf", new XElement(d + "Rsn", new XElement(d + "Cd", x.ReasonCode))),
                            new XElement(d + "OrgnlTxRef", new XElement(d + "DbtrAgt", new XElement(d + "FinInstnId", new XElement(d + "BICFI", x.OriginalSenderBic))))
                        )))));
        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>R2: camt.056.001.09 FIToFIPmtCxlReq delivered by the gateway (SipsInboundRecallMessage.Build), with exactly one Undrlyg/TxInf.</summary>
    public static PapssInboundRecallMessage ParseInboundRecall(string xml)
    {
        var (header, document) = M.Load(xml, "FIToFIPmtCxlReq");
        var tx = M.Children(document, "Undrlyg").SelectMany(x => M.Children(x, "TxInf")).ToList();
        if (tx.Count != 1) throw new InvalidDataException($"The PAPSS inbound recall notification must carry exactly one Undrlyg/TxInf (found {tx.Count}).");
        var t = tx[0];
        var reason = M.Child(t, "CxlRsnInf");
        return new PapssInboundRecallMessage(
            M.Required(M.Text(header, "BizMsgIdr"), "AppHdr BizMsgIdr"),
            M.Timestamp(M.Text(header, "CreDt")),
            M.Required(M.Text(t, "CxlId"), "CxlId"),
            M.Required(M.Text(t, "OrgnlTxId"), "OrgnlTxId"),
            M.Required(M.Text(t, "OrgnlEndToEndId"), "OrgnlEndToEndId"),
            M.ReasonCode(M.Child(reason, "Rsn")),
            M.Text(reason, "AddtlInf"),
            M.Provenance(header, t));
    }

    /// <summary>camt.029.001.09 RsltnOfInvstgtn with exactly one CxlDtls/TxInfAndSts.</summary>
    public static PapssRecallResolution ParseResolution(string xml)
    {
        var (header, document) = M.Load(xml, "RsltnOfInvstgtn");
        var tx = M.Children(document, "CxlDtls").SelectMany(x => M.Children(x, "TxInfAndSts")).ToList();
        if (tx.Count != 1) throw new InvalidDataException($"The PAPSS camt.029 must carry exactly one CxlDtls/TxInfAndSts (found {tx.Count}).");
        var t = tx[0];
        var reason = M.Child(t, "CxlStsRsnInf");
        var group = M.Child(t, "OrgnlGrpInf");
        return new PapssRecallResolution(
            M.Required(M.Text(header, "BizMsgIdr"), "AppHdr BizMsgIdr"),
            M.Timestamp(M.Text(header, "CreDt")),
            M.Text(M.Child(document, "Assgnmt"), "Id"),
            M.Text(M.Child(document, "RslvdCase"), "Id"),
            M.Text(M.Child(document, "Sts"), "Conf")?.ToUpperInvariant(),
            M.Text(t, "CxlStsId"),
            M.Text(group, "OrgnlMsgId"),
            M.Text(group, "OrgnlMsgNmId"),
            M.Required(M.Text(t, "OrgnlTxId"), "OrgnlTxId"),
            M.Text(t, "OrgnlEndToEndId"),
            M.ReasonCode(M.Child(reason, "Rsn")),
            M.Text(reason, "AddtlInf"),
            M.Provenance(header, t));
    }

    /// <summary>TxInfAndSts/StsId of a pacs.002 (the PAPSS status id), when present.</summary>
    public static string? StatusId(string pacs002)
    {
        var (_, document) = M.Load(pacs002, "FIToFIPmtStsRpt");
        return M.Text(M.Children(document, "TxInfAndSts").FirstOrDefault(), "StsId");
    }

    /// <summary>
    /// The audit record of a manual operator close, stored as the RECALL_CLOSED event's "raw" bytes (JSON, not XML: there is no
    /// PAPSS/gateway message for this action). <see cref="BuildCloseAudit"/> and <see cref="ParseCloseAudit"/> round-trip it.
    /// </summary>
    public sealed record PapssRecallCloseAudit(string ClosedBy, string Reason, DateTimeOffset ClosedAt, string AuthPath);

    public static byte[] BuildCloseAudit(PapssRecallCloseAudit audit)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(audit);

    public static PapssRecallCloseAudit ParseCloseAudit(byte[] raw)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<PapssRecallCloseAudit>(raw)
                ?? throw new InvalidDataException("The recall close audit record could not be read.");
        }
        catch (System.Text.Json.JsonException error)
        {
            throw new InvalidDataException("The recall close audit record could not be read.", error);
        }
    }
}

/// <summary>
/// Recall state rules. Open: RECALL_PENDING, RECALL_ACCEPTED_BY_PAPSS, RECALL_OUTCOME_UNRESOLVED. Final:
/// RECALL_REJECTED_BY_PAPSS, RECALL_REJECTED_BY_BENEFICIARY, RECALL_RETURNED, RECALL_ABANDONED (and REJECTED when the gateway
/// refused the camt.056). An answer is applied only to an open recall; the same final answer again is DUPLICATE_FINAL; a
/// contradicting answer is flagged (CONFLICT). A recall stuck at RECALL_OUTCOME_UNRESOLVED can still be resolved normally by a
/// later, legitimate camt.029/pacs.004 answer; the manual close (RECALL_ABANDONED) is a fallback for when none ever arrives.
/// </summary>
public static class PapssRecallRules
{
    public static readonly PapssOutcome[] OpenOutcomes = [PapssOutcome.RecallPending, PapssOutcome.RecallAcceptedByPapss, PapssOutcome.RecallOutcomeUnresolved];

    public static bool IsOpen(PapssOutcome outcome) => outcome is PapssOutcome.RecallPending or PapssOutcome.RecallAcceptedByPapss or PapssOutcome.RecallOutcomeUnresolved;

    /// <summary>The recall outcome a PAPSS pacs.002 TxSts means (ACCP / RJCT only; TxSts is ISO-capped at 4 characters).</summary>
    public static PapssOutcome? OutcomeOfPapssStatus(string? status) => status switch
    {
        PapssPaymentStatusRules.ACCP => PapssOutcome.RecallAcceptedByPapss,
        PapssPaymentStatusRules.RJCT => PapssOutcome.RecallRejectedByPapss,
        _ => null
    };

    /// <summary>
    /// The recall outcome a PAPSS pacs.002 answer means: the gateway's RECALL_OUTCOME_UNRESOLVED signal (carried in the reason,
    /// see <see cref="PapssRecallMessages.UnresolvedStatus"/>) takes precedence over TxSts; otherwise ACCP / RJCT from TxSts.
    /// </summary>
    public static PapssOutcome? OutcomeOfPapssAnswer(string? status, string? reasonCode)
        => string.Equals(reasonCode, PapssRecallMessages.UnresolvedStatus, StringComparison.OrdinalIgnoreCase)
            ? PapssOutcome.RecallOutcomeUnresolved
            : OutcomeOfPapssStatus(status);

    /// <summary>What PAPSS's immediate pacs.002 (ACCP/RJCT, or the gateway's "outcome unresolved" signal) does to a recall currently in <paramref name="current"/>.</summary>
    public static string EvaluatePapssStatus(PapssOutcome current, string? status, string? reasonCode)
    {
        if (OutcomeOfPapssAnswer(status, reasonCode) is not { } next) return PapssEventDisposition.UnknownStatus;
        // "Unresolved" conveys no more than "PAPSS's outcome could not be determined": from RECALL_PENDING it is new
        // information (the recall is now known to be open-but-unresolved, Applied); from anywhere else it is strictly less
        // specific than what is already known, so it never contradicts anything and is always harmless history.
        if (next == PapssOutcome.RecallOutcomeUnresolved)
            return current == PapssOutcome.RecallPending ? PapssEventDisposition.Applied : PapssEventDisposition.NotAdvancing;
        return current switch
        {
            PapssOutcome.RecallPending => PapssEventDisposition.Applied,
            // A definite ACCP/RJCT now available is a legitimate clarification of an unresolved answer.
            PapssOutcome.RecallOutcomeUnresolved => PapssEventDisposition.Applied,
            PapssOutcome.RecallAcceptedByPapss => next == PapssOutcome.RecallAcceptedByPapss ? PapssEventDisposition.NotAdvancing : PapssEventDisposition.Conflict,
            PapssOutcome.RecallRejectedByPapss => next == PapssOutcome.RecallRejectedByPapss ? PapssEventDisposition.DuplicateFinal : PapssEventDisposition.Conflict,
            // The beneficiary already answered (or the recall was manually closed): a late ACCP is history, a late RJCT contradicts it.
            PapssOutcome.RecallRejectedByBeneficiary or PapssOutcome.RecallReturned or PapssOutcome.RecallAbandoned =>
                next == PapssOutcome.RecallAcceptedByPapss ? PapssEventDisposition.NotAdvancing : PapssEventDisposition.Conflict,
            _ => PapssEventDisposition.Conflict
        };
    }

    /// <summary>What a camt.029 (Conf) does to a recall currently in <paramref name="current"/>.</summary>
    public static string EvaluateResolution(PapssOutcome current, string? confirmation)
    {
        if (!string.Equals(confirmation, PapssRecallMessages.RejectedConfirmation, StringComparison.Ordinal)) return PapssEventDisposition.UnknownStatus;
        if (IsOpen(current)) return PapssEventDisposition.Applied;
        return current == PapssOutcome.RecallRejectedByBeneficiary ? PapssEventDisposition.DuplicateFinal : PapssEventDisposition.Conflict;
    }

    /// <summary>The outcome a stored recall answer event reports to the bank (independent of later answers).</summary>
    public static PapssOutcome? OutcomeOfEvent(string eventType, string? status, string? reasonCode) => eventType switch
    {
        PapssEventTypes.RecallStatus => OutcomeOfPapssAnswer(status, reasonCode),
        PapssEventTypes.RecallResolution => PapssOutcome.RecallRejectedByBeneficiary,
        PapssEventTypes.RecallReturned => PapssOutcome.RecallReturned,
        PapssEventTypes.RecallClosed => PapssOutcome.RecallAbandoned,
        _ => null
    };
}
