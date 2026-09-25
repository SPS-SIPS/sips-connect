using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SIPS.PostgreSQL.Enums;

namespace SIPS.Connect.Services;

/// <summary>pacs.002.001.12 delivered by the gateway for a PAPSS payment, return or status enquiry.</summary>
public sealed record PapssStatusReport(
    string SourceMessageId,
    DateTimeOffset? SourceCreatedAt,
    string? MsgId,
    string OriginalMessageId,
    string? OriginalMessageType,
    string OriginalTxId,
    string? OriginalEndToEndId,
    string? Status,
    string? ReasonCode,
    string? AdditionalInfo,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? AcceptedAt);

/// <summary>pacs.004.001.11 delivered by the gateway: a return of a payment this participant sent.</summary>
public sealed record PapssReturnMessage(
    string SourceMessageId,
    DateTimeOffset? SourceCreatedAt,
    string? MsgId,
    string ReturnId,
    string? OriginalMessageId,
    string? OriginalMessageType,
    string OriginalTxId,
    string OriginalEndToEndId,
    decimal? Amount,
    string? Currency,
    string? ReasonCode,
    string? AdditionalInfo,
    string? LocalInstrument,
    string? InstructingAgent);

/// <summary>pacs.008.001.10 delivered by the gateway: a credit transfer received from PAPSS.</summary>
public sealed record PapssPaymentMessage(
    string SourceMessageId,
    DateTimeOffset? SourceCreatedAt,
    string? MsgId,
    string TxId,
    string? EndToEndId,
    decimal? Amount,
    string? Currency,
    string? LocalInstrument,
    string? DebtorAgent);

/// <summary>
/// Reads the gateway's SIPS-profile pacs messages by exact element path (never by "any descendant with
/// this name", which is ambiguous for Cd/Prtry: see evidence §B6). Reason codes are read from Cd or Prtry.
/// </summary>
public static class PapssPaymentMessages
{
    public const string Pacs002 = "pacs.002.001.12";
    public const string Pacs004 = "pacs.004.001.11";
    public const string Pacs008 = "pacs.008.001.10";
    public const string Pacs028 = "pacs.028.001.05";

    public static PapssStatusReport ParseStatusReport(string xml)
    {
        var (header, document) = Load(xml, "FIToFIPmtStsRpt");
        var tx = Children(document, "TxInfAndSts").ToList();
        if (tx.Count != 1) throw new InvalidDataException($"The PAPSS pacs.002 must carry exactly one TxInfAndSts (found {tx.Count}).");
        var t = tx[0];
        var group = Child(document, "OrgnlGrpInfAndSts");
        var reason = Child(t, "StsRsnInf");
        var amount = Path(t, "OrgnlTxRef", "IntrBkSttlmAmt") ?? Path(t, "OrgnlTxRef", "Amt", "InstdAmt");
        return new PapssStatusReport(
            Required(Text(header, "BizMsgIdr"), "AppHdr BizMsgIdr"),
            Timestamp(Text(header, "CreDt")),
            Text(Child(document, "GrpHdr"), "MsgId"),
            Required(Text(Child(t, "OrgnlGrpInf"), "OrgnlMsgId") ?? Text(group, "OrgnlMsgId"), "OrgnlMsgId"),
            Text(Child(t, "OrgnlGrpInf"), "OrgnlMsgNmId") ?? Text(group, "OrgnlMsgNmId"),
            Required(Text(t, "OrgnlTxId"), "OrgnlTxId"),
            Text(t, "OrgnlEndToEndId"),
            (Text(t, "TxSts") ?? Text(group, "GrpSts"))?.ToUpperInvariant(),
            ReasonCode(Child(reason, "Rsn")),
            Text(reason, "AddtlInf"),
            Amount(amount),
            Currency(amount),
            Timestamp(Text(t, "AccptncDtTm")));
    }

    public static PapssReturnMessage ParseReturn(string xml)
    {
        var (header, document) = Load(xml, "PmtRtr");
        var group = Child(document, "GrpHdr");
        var tx = Children(document, "TxInf").ToList();
        if (tx.Count != 1) throw new InvalidDataException($"The PAPSS pacs.004 must carry exactly one TxInf (found {tx.Count}).");
        var t = tx[0];
        var reason = Child(t, "RtrRsnInf");
        var amount = Child(t, "RtrdIntrBkSttlmAmt");
        var instrument = Path(group, "PmtTpInf", "LclInstrm") ?? Path(t, "OrgnlTxRef", "PmtTpInf", "LclInstrm");
        return new PapssReturnMessage(
            Required(Text(header, "BizMsgIdr"), "AppHdr BizMsgIdr"),
            Timestamp(Text(header, "CreDt")),
            Text(group, "MsgId"),
            Required(Text(t, "RtrId"), "RtrId"),
            Text(Child(t, "OrgnlGrpInf"), "OrgnlMsgId"),
            Text(Child(t, "OrgnlGrpInf"), "OrgnlMsgNmId"),
            Required(Text(t, "OrgnlTxId"), "OrgnlTxId"),
            Required(Text(t, "OrgnlEndToEndId"), "OrgnlEndToEndId"),
            Amount(amount),
            Currency(amount),
            ReasonCode(Child(reason, "Rsn")),
            Text(reason, "AddtlInf"),
            ReasonCode(instrument),
            Agent(Child(group, "InstgAgt")));
    }

    public static PapssPaymentMessage ParsePayment(string xml)
    {
        var (header, document) = Load(xml, "FIToFICstmrCdtTrf");
        var group = Child(document, "GrpHdr");
        var tx = Children(document, "CdtTrfTxInf").ToList();
        if (tx.Count != 1) throw new InvalidDataException($"The PAPSS pacs.008 must carry exactly one CdtTrfTxInf (found {tx.Count}).");
        var t = tx[0];
        var amount = Child(t, "IntrBkSttlmAmt");
        var instrument = Path(group, "PmtTpInf", "LclInstrm") ?? Path(t, "PmtTpInf", "LclInstrm");
        return new PapssPaymentMessage(
            Required(Text(header, "BizMsgIdr"), "AppHdr BizMsgIdr"),
            Timestamp(Text(header, "CreDt")),
            Text(group, "MsgId"),
            Required(Text(Child(t, "PmtId"), "TxId"), "TxId"),
            Text(Child(t, "PmtId"), "EndToEndId"),
            Amount(amount),
            Currency(amount),
            ReasonCode(instrument),
            Agent(Child(t, "DbtrAgt")));
    }

    /// <summary>TxSts of a stored pacs.002 (e.g. the bank decision for an inbound payment) and its reason.</summary>
    public static (string? Status, string? Reason) DecisionStatus(string xml)
    {
        var (_, document) = Load(xml, "FIToFIPmtStsRpt");
        var t = Children(document, "TxInfAndSts").FirstOrDefault();
        return ((Text(t, "TxSts") ?? Text(Child(document, "OrgnlGrpInfAndSts"), "GrpSts"))?.ToUpperInvariant(), ReasonCode(Path(t, "StsRsnInf", "Rsn")));
    }

    // ----------------------------------------------------------------------------------------------

    private static (XElement Header, XElement Document) Load(string xml, string documentRoot)
    {
        XDocument parsed;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            parsed = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException error) { throw new InvalidDataException("The PAPSS message is not well-formed XML.", error); }
        var header = parsed.Descendants().SingleOrDefault(x => x.Name.LocalName == "AppHdr") ?? throw new InvalidDataException("The PAPSS message has no AppHdr.");
        var document = parsed.Descendants().SingleOrDefault(x => x.Name.LocalName == documentRoot) ?? throw new InvalidDataException($"The PAPSS message has no {documentRoot}.");
        return (header, document);
    }

    private static IEnumerable<XElement> Children(XElement? parent, string name) => parent?.Elements().Where(x => x.Name.LocalName == name) ?? [];
    private static XElement? Child(XElement? parent, string name) => Children(parent, name).FirstOrDefault();
    private static XElement? Path(XElement? parent, params string[] names) => names.Aggregate(parent, (current, name) => Child(current, name));
    private static string? Text(XElement? parent, string name) => NullIfEmpty(Child(parent, name)?.Value);
    private static string? ReasonCode(XElement? choice) => Text(choice, "Cd") ?? Text(choice, "Prtry");
    private static string? Agent(XElement? agent)
    {
        var institution = Child(agent, "FinInstnId");
        return Text(institution, "BICFI") ?? Text(Child(institution, "Othr"), "Id") ?? Text(Child(institution, "ClrSysMmbId"), "MmbId");
    }

    private static decimal? Amount(XElement? amount)
        => amount is not null && decimal.TryParse(amount.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? Currency(XElement? amount)
    {
        var code = amount?.Attribute("Ccy")?.Value.Trim().ToUpperInvariant();
        return code is { Length: 3 } && code.All(c => c is >= 'A' and <= 'Z') ? code : null;
    }

    private static DateTimeOffset? Timestamp(string? value)
        => value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Required(string? value, string name) => value ?? throw new InvalidDataException($"The PAPSS message is missing {name}.");
}

/// <summary>
/// Payment outcome rules. PAPSS material contradicts itself on how many pacs.002 a payment gets and in
/// which order (evidence §B5), so every status is accepted as an event and applied only when it advances
/// the payment: a final state (SETTLED, REJECTED, RETURNED) is never regressed, and a different final
/// status after a final one is flagged instead of overwriting it.
/// </summary>
public static class PapssPaymentStatusRules
{
    public const string ACCP = "ACCP", ACSP = "ACSP", ACSC = "ACSC", PDNG = "PDNG", RJCT = "RJCT";

    public static bool IsFinal(PapssOutcome outcome) => outcome is PapssOutcome.Settled or PapssOutcome.Rejected or PapssOutcome.Returned;

    /// <summary>
    /// Maps a raw ISO status to the payment outcome. For a RETURN operation, <paramref name="returnSettledStatuses"/>
    /// decides which statuses settle it (PAPSS: ACSC vs ACSP is CONTRADICTED, evidence §D10).
    /// </summary>
    public static PapssOutcome? OutcomeOf(string? status, bool isReturn, IReadOnlyCollection<string> returnSettledStatuses)
    {
        if (status is null) return null;
        if (isReturn && returnSettledStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) return PapssOutcome.Settled;
        return status switch
        {
            ACCP or ACSP => PapssOutcome.Accepted,
            ACSC => PapssOutcome.Settled,
            RJCT => PapssOutcome.Rejected,
            PDNG => PapssOutcome.Pending,
            _ => null
        };
    }

    private static int Rank(string? status, PapssOutcome outcome) => IsFinal(outcome) ? 3 : status switch
    {
        PDNG => 0,
        ACCP => 1,
        ACSP => 2,
        _ => -1
    };

    /// <summary>What a newly received status does to an operation currently in (<paramref name="outcome"/>, <paramref name="rawStatus"/>).</summary>
    public static string Evaluate(PapssOutcome outcome, string? rawStatus, string? status, bool isReturn, IReadOnlyCollection<string> returnSettledStatuses)
    {
        var next = OutcomeOf(status, isReturn, returnSettledStatuses);
        if (next is not { } nextOutcome) return PapssEventDisposition.UnknownStatus;
        if (IsFinal(outcome))
        {
            if (!IsFinal(nextOutcome)) return PapssEventDisposition.NotAdvancing;
            var same = nextOutcome == outcome || outcome == PapssOutcome.Returned && nextOutcome == PapssOutcome.Settled;
            return same ? PapssEventDisposition.DuplicateFinal : PapssEventDisposition.Conflict;
        }
        if (IsFinal(nextOutcome)) return PapssEventDisposition.Applied;
        return Rank(status, nextOutcome) > Rank(rawStatus, outcome) ? PapssEventDisposition.Applied : PapssEventDisposition.NotAdvancing;
    }
}
