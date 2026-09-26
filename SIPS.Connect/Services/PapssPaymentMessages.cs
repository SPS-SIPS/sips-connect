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
    DateTimeOffset? AcceptedAt,
    PapssProvenance? Provenance = null)
{
    /// <summary>
    /// Where the gateway got OrgnlTxRef/IntrBkSttlmAmt: NETWORK_REPORTED (PAPSS sent it) or LOCAL_RECONSTRUCTION (borrowed from
    /// the original payment; never a PAPSS-reported amount). UNSPECIFIED_LEGACY when the gateway did not say.
    /// </summary>
    public string AmountSource => Provenance?.SourceOf(PapssFieldProvenance.StatusAmountPath) ?? PapssFieldProvenance.UnspecifiedLegacy;
}

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
    string? InstructingAgent,
    PapssProvenance? Provenance = null)
{
    public string AmountSource => Provenance?.SourceOf(PapssFieldProvenance.ReturnAmountPath) ?? PapssFieldProvenance.UnspecifiedLegacy;
    /// <summary>PAPSS pacs.004 carries no CtgyPurp: LOCAL_RECONSTRUCTION means the gateway took it from the original payment.</summary>
    public string CategoryPurposeSource => Provenance?.SourceOf(PapssFieldProvenance.ReturnCategoryPurposePath) ?? PapssFieldProvenance.UnspecifiedLegacy;
}

/// <summary>
/// The gateway's signed SPS provenance supplement (TxInfAndSts/TxInf SplmtryData, urn:sps:papss:provenance:001). Links the
/// three evidence layers: the raw PAPSS message (<see cref="SourceMessageId"/> and <see cref="RawEvidenceReference"/>, the
/// SHA-256 of the signed PAPSS bytes the gateway keeps in custody), the normalized event SIPS Connect stores, and the per-field
/// origin of every value the gateway did not simply relay (<see cref="Fields"/>, path relative to FIToFIPmtStsRpt / PmtRtr).
/// </summary>
public sealed record PapssProvenance(string SourceMessageId, string RawEvidenceReference, IReadOnlyDictionary<string, string> Fields)
{
    public string? SourceOf(string path) => Fields.TryGetValue(path, out var source) ? source : null;
}

/// <summary>Per-field provenance vocabulary shared with the gateway (CallbackFieldProvenance).</summary>
public static class PapssFieldProvenance
{
    public const string Namespace = "urn:sps:papss:provenance:001";
    /// <summary>PAPSS transmitted the value: eligible for the reported-vs-stored amount comparison.</summary>
    public const string NetworkReported = "NETWORK_REPORTED";
    /// <summary>The gateway reconstructed the value from its stored state; PAPSS did not report it.</summary>
    public const string LocalReconstruction = "LOCAL_RECONSTRUCTION";
    public const string IdentifierTranslation = "IDENTIFIER_TRANSLATION";
    public const string DefaultFiller = "DEFAULT_FILLER";
    /// <summary>SIPS Connect only: the callback carried no provenance for the field (older gateway or legacy effect).</summary>
    public const string UnspecifiedLegacy = "UNSPECIFIED_LEGACY";
    public const string StatusAmountPath = "TxInfAndSts/OrgnlTxRef/IntrBkSttlmAmt";
    public const string ReturnAmountPath = "TxInf/RtrdIntrBkSttlmAmt";
    public const string ReturnCategoryPurposePath = "GrpHdr/PmtTpInf/CtgyPurp";
    private static readonly HashSet<string> Transmitted = new(StringComparer.Ordinal) { NetworkReported, LocalReconstruction, IdentifierTranslation, DefaultFiller };

    /// <summary>Whether an amount of this origin may be compared with (or fill) the stored operation amount.</summary>
    public static bool IsReportedAmount(string source) => source is NetworkReported or UnspecifiedLegacy;

    internal static bool IsKnown(string source) => Transmitted.Contains(source);
}

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
            Timestamp(Text(t, "AccptncDtTm")),
            Provenance(header, t));
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
            Agent(Child(group, "InstgAgt")),
            Provenance(header, t));
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

    internal static (XElement Header, XElement Document) Load(string xml, string documentRoot)
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

    /// <summary>
    /// The provenance supplement, only where the gateway puts it (the transaction's SplmtryData/Envlp) and only once. It must
    /// name the same PAPSS source message as the AppHdr; an unknown source value is refused rather than guessed.
    /// </summary>
    internal static PapssProvenance? Provenance(XElement header, XElement transaction)
    {
        XNamespace p = PapssFieldProvenance.Namespace;
        var all = header.Document!.Descendants(p + "PapssProvenance").ToList();
        if (all.Count == 0) return null;
        if (all.Count > 1) throw new InvalidDataException("The PAPSS callback carries more than one provenance supplement.");
        var envelope = all[0];
        if (envelope.Parent?.Name.LocalName != "Envlp" || envelope.Parent.Parent?.Name.LocalName != "SplmtryData" || envelope.Parent.Parent.Parent != transaction)
            throw new InvalidDataException("The PAPSS provenance supplement is not in the transaction's SplmtryData.");
        string Value(XElement parent, string name) => NullIfEmpty(parent.Element(p + name)?.Value) ?? throw new InvalidDataException($"The PAPSS provenance supplement is missing {name}.");
        var source = Value(envelope, "SourceMessageId");
        if (!string.Equals(source, Text(header, "BizMsgIdr"), StringComparison.Ordinal))
            throw new InvalidDataException("The PAPSS provenance supplement names another source message than the AppHdr.");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in envelope.Elements(p + "Field"))
        {
            var path = Value(field, "Path");
            var origin = Value(field, "Source");
            if (!PapssFieldProvenance.IsKnown(origin)) throw new InvalidDataException($"Unknown PAPSS field provenance '{origin}' for {path}.");
            if (!fields.TryAdd(path, origin)) throw new InvalidDataException($"The PAPSS provenance supplement repeats {path}.");
        }
        return new PapssProvenance(source, Value(envelope, "RawEvidenceReference"), fields);
    }

    internal static IEnumerable<XElement> Children(XElement? parent, string name) => parent?.Elements().Where(x => x.Name.LocalName == name) ?? [];
    internal static XElement? Child(XElement? parent, string name) => Children(parent, name).FirstOrDefault();
    internal static XElement? Path(XElement? parent, params string[] names) => names.Aggregate(parent, (current, name) => Child(current, name));
    internal static string? Text(XElement? parent, string name) => NullIfEmpty(Child(parent, name)?.Value);
    internal static string? ReasonCode(XElement? choice) => Text(choice, "Cd") ?? Text(choice, "Prtry");
    internal static string? Agent(XElement? agent)
    {
        var institution = Child(agent, "FinInstnId");
        return Text(institution, "BICFI") ?? Text(Child(institution, "Othr"), "Id") ?? Text(Child(institution, "ClrSysMmbId"), "MmbId");
    }

    internal static decimal? Amount(XElement? amount)
        => amount is not null && decimal.TryParse(amount.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    internal static string? Currency(XElement? amount)
    {
        var code = amount?.Attribute("Ccy")?.Value.Trim().ToUpperInvariant();
        return code is { Length: 3 } && code.All(c => c is >= 'A' and <= 'Z') ? code : null;
    }

    internal static DateTimeOffset? Timestamp(string? value)
        => value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    internal static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static string Required(string? value, string name) => value ?? throw new InvalidDataException($"The PAPSS message is missing {name}.");
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
