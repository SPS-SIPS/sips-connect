using System.Xml;
using System.Xml.Serialization;

namespace SIPS.ISO20022.Models.WpSips;

public static class WpSipsNamespaces
{
    public const string Header = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    public const string Admi002 = "urn:iso:std:iso:20022:tech:xsd:admi.002.001.01";
    public const string Admi009 = "urn:iso:std:iso:20022:tech:xsd:admi.009.001.02";
    public const string Admi010 = "urn:iso:std:iso:20022:tech:xsd:admi.010.001.02";
    public const string FxRequest = "urn:sps:papss:fx:001:request";
    public const string FxResponse = "urn:sps:papss:fx:001:response";
    public const string ParticipantRequest = "urn:sps:papss:participant:001:request";
    public const string ParticipantResponse = "urn:sps:papss:participant:001:response";
    public const string ReadinessRequest = "urn:sps:papss:readiness:001:request";
    public const string ReadinessResponse = "urn:sps:papss:readiness:001:response";
}

public static class WpSipsProfiles
{
    public const string Fx = "SPS.PAPSS.FX.001";
    public const string Participant = "SPS.PAPSS.PARTICIPANT.001";
    public const string Readiness = "SPS.PAPSS.READINESS.001";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Fx, Participant, Readiness };
}

[XmlRoot("Document", Namespace = WpSipsNamespaces.Admi009)]
public sealed class StaticDataRequestDocument
{
    [XmlElement("StatcDataReq", Order = 0)] public StaticDataRequestV02 Request { get; set; } = new();
}

public sealed class StaticDataRequestV02
{
    [XmlElement("MsgId", Order = 0)] public string MessageId { get; set; } = "";
    [XmlElement("SttlmSsnIdr", Order = 1)] public string? SettlementSessionId { get; set; }
    [XmlElement("DataReqDtls", Order = 2)] public StaticDataRequestDetails Details { get; set; } = new();
    [XmlElement("SplmtryData", Order = 3)] public SupplementaryData[] SupplementaryData { get; set; } = [];
}

public sealed class StaticDataRequestDetails
{
    [XmlElement("Tp", Order = 0)] public string Type { get; set; } = "";
    [XmlElement("Key", Order = 1)] public string? Key { get; set; }
}

[XmlRoot("Document", Namespace = WpSipsNamespaces.Admi010)]
public sealed class StaticDataReportDocument
{
    [XmlElement("StatcDataRpt", Order = 0)] public StaticDataReportV02 Report { get; set; } = new();
}

public sealed class StaticDataReportV02
{
    [XmlElement("MsgId", Order = 0)] public string MessageId { get; set; } = "";
    [XmlElement("SttlmSsnIdr", Order = 1)] public string? SettlementSessionId { get; set; }
    [XmlElement("RptDtls", Order = 2)] public StaticDataReportDetails Details { get; set; } = new();
    [XmlElement("SplmtryData", Order = 3)] public SupplementaryData[] SupplementaryData { get; set; } = [];
}

public sealed class StaticDataReportDetails
{
    [XmlElement("Tp", Order = 0)] public string Type { get; set; } = "";
    [XmlElement("ReqRef", Order = 1)] public string RequestReference { get; set; } = "";
    [XmlElement("RptKey", Order = 2)] public StaticDataReportKey[] Keys { get; set; } = [];
}

public sealed class StaticDataReportKey
{
    [XmlElement("Key", Order = 0)] public string Key { get; set; } = "";
    [XmlElement("RptData", Order = 1)] public StaticDataReportParameter[] Parameters { get; set; } = [];
}

public sealed class StaticDataReportParameter
{
    [XmlElement("Nm", Order = 0)] public string Name { get; set; } = "";
    [XmlElement("Val", Order = 1)] public string Value { get; set; } = "";
}

public sealed class SupplementaryData
{
    [XmlElement("PlcAndNm", Order = 0)] public string? PlaceAndName { get; set; }
    [XmlElement("Envlp", Order = 1)] public SupplementaryEnvelope Envelope { get; set; } = new();
}

public sealed class SupplementaryEnvelope
{
    [XmlAnyElement] public XmlElement? Content { get; set; }
}

[XmlRoot("Document", Namespace = WpSipsNamespaces.Admi002)]
public sealed class MessageRejectDocument
{
    [XmlElement("admi.002.001.01", Order = 0)] public MessageRejectV01 Reject { get; set; } = new();
}

public sealed class MessageRejectV01
{
    [XmlElement("RltdRef", Order = 0)] public MessageReference RelatedReference { get; set; } = new();
    [XmlElement("Rsn", Order = 1)] public RejectionReason Reason { get; set; } = new();
}
public sealed class MessageReference { [XmlElement("Ref")] public string Reference { get; set; } = ""; }
public sealed class RejectionReason
{
    [XmlElement("RjctgPtyRsn", Order = 0)] public string Code { get; set; } = "";
    [XmlElement("RjctnDtTm", Order = 1)] public DateTime? RejectedAt { get; set; }
    [XmlElement("ErrLctn", Order = 2)] public string? ErrorLocation { get; set; }
    [XmlElement("RsnDesc", Order = 3)] public string? Description { get; set; }
}

public sealed record FxRateRequest(string SenderCountry, string ReceiverCountry, string SenderCurrency, string ReceiverCurrency, string ReceiverBank, string LocalInstrument, decimal Amount, bool IsInvoice, string? InvoiceCurrency);
public sealed record CurrencyAmount(decimal Value, string Currency);
public sealed record FxRate(decimal Value, string Type, DateTimeOffset? UpdateTime);
public sealed record FxRateResponse(IReadOnlyList<FxRate> Rates, CurrencyAmount SenderAmount, CurrencyAmount ExchangeAmount, CurrencyAmount ReceiverAmount, CurrencyAmount? NationalFeeAmount, CurrencyAmount? FeeAmount, ServiceError? Error = null);
public sealed record ParticipantDiscoveryRequest(bool? Online, string? Type, string? Bic, string? PapssId);
public sealed record Participant(string PapssId, string? Bic, string Name, string CountryCode, string Status, IReadOnlyList<string> PaymentSchemas, IReadOnlyList<string> Currencies, bool Online, bool NonInstant);
public sealed record ParticipantDiscoveryResponse(IReadOnlyList<Participant> Participants, ServiceError? Error = null);
public sealed record ReadinessRequest(string? PapssId, string? Bic);
public sealed record ReadinessResponse(Participant? Observation, ServiceError? Error = null);
public sealed record ServiceError(string Authority, string Code, string? Description);

public sealed record BusinessHeader(string From, string To, string BusinessMessageId, string MessageDefinitionId, string BusinessService, DateTimeOffset CreatedAt, string? RelatedBusinessMessageId = null, string? RelatedMessageDefinitionId = null, string? RelatedBusinessService = null, DateTimeOffset? RelatedCreatedAt = null);
public sealed record WpSipsMessage<T>(BusinessHeader Header, string ServiceMessageId, string? RequestReference, T Payload);
public sealed record AdminReject(string RejectedBusinessMessageId, string ReasonCode, DateTimeOffset RejectedAt, string? ErrorLocation, string? Description);
