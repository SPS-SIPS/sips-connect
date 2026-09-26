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
    /// <summary>RECALL_ACCEPTED_BY_PAPSS, RECALL_REJECTED_BY_PAPSS, RECALL_REJECTED_BY_BENEFICIARY or RECALL_RETURNED.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? ReasonCode { get; set; }
    /// <summary>The answering party's id: pacs.002 StsId, camt.029 CxlStsId or pacs.004 RtrId.</summary>
    public string? ResponderId { get; set; }
    public string SourceMessageId { get; set; } = string.Empty;
    public string ReceivedAt { get; set; } = string.Empty;
}

/// <summary>
/// Builds the outbound camt.056.001.08 recall request (there are no camt builders or schemas in SIPS.ISO20022) and reads the
/// camt.029.001.09 answer, by exact element path like <see cref="PapssPaymentMessages"/>.
/// </summary>
public static partial class PapssRecallMessages
{
    public const string Camt056 = "camt.056.001.08";
    public const string Camt029 = "camt.029.001.09";
    public const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    public const string Camt056Namespace = "urn:iso:std:iso:20022:tech:xsd:" + Camt056;
    /// <summary>Root of the SIPS envelope. The gateway reads AppHdr and Document under the root and does not check the root name.</summary>
    public const string EnvelopeNamespace = "urn:iso:std:iso:20022:tech:xsd:paymentRecall_request";
    public const string RejectedConfirmation = "RJCR";

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
}

/// <summary>
/// Recall state rules. Open: RECALL_PENDING, RECALL_ACCEPTED_BY_PAPSS. Final: RECALL_REJECTED_BY_PAPSS,
/// RECALL_REJECTED_BY_BENEFICIARY, RECALL_RETURNED (and REJECTED when the gateway refused the camt.056). An answer is applied
/// only to an open recall; the same final answer again is DUPLICATE_FINAL; a contradicting answer is flagged (CONFLICT).
/// </summary>
public static class PapssRecallRules
{
    public static readonly PapssOutcome[] OpenOutcomes = [PapssOutcome.RecallPending, PapssOutcome.RecallAcceptedByPapss];

    public static bool IsOpen(PapssOutcome outcome) => outcome is PapssOutcome.RecallPending or PapssOutcome.RecallAcceptedByPapss;

    /// <summary>The recall outcome a PAPSS pacs.002 status means (ACCP / RJCT only).</summary>
    public static PapssOutcome? OutcomeOfPapssStatus(string? status) => status switch
    {
        PapssPaymentStatusRules.ACCP => PapssOutcome.RecallAcceptedByPapss,
        PapssPaymentStatusRules.RJCT => PapssOutcome.RecallRejectedByPapss,
        _ => null
    };

    /// <summary>What PAPSS's immediate pacs.002 (ACCP/RJCT) does to a recall currently in <paramref name="current"/>.</summary>
    public static string EvaluatePapssStatus(PapssOutcome current, string? status)
    {
        if (OutcomeOfPapssStatus(status) is not { } next) return PapssEventDisposition.UnknownStatus;
        return current switch
        {
            PapssOutcome.RecallPending => PapssEventDisposition.Applied,
            PapssOutcome.RecallAcceptedByPapss => next == PapssOutcome.RecallAcceptedByPapss ? PapssEventDisposition.NotAdvancing : PapssEventDisposition.Conflict,
            PapssOutcome.RecallRejectedByPapss => next == PapssOutcome.RecallRejectedByPapss ? PapssEventDisposition.DuplicateFinal : PapssEventDisposition.Conflict,
            // The beneficiary already answered: a late ACCP is history, a late RJCT contradicts it.
            PapssOutcome.RecallRejectedByBeneficiary or PapssOutcome.RecallReturned => next == PapssOutcome.RecallAcceptedByPapss ? PapssEventDisposition.NotAdvancing : PapssEventDisposition.Conflict,
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
    public static PapssOutcome? OutcomeOfEvent(string eventType, string? status) => eventType switch
    {
        PapssEventTypes.RecallStatus => OutcomeOfPapssStatus(status),
        PapssEventTypes.RecallResolution => PapssOutcome.RecallRejectedByBeneficiary,
        PapssEventTypes.RecallReturned => PapssOutcome.RecallReturned,
        _ => null
    };
}
