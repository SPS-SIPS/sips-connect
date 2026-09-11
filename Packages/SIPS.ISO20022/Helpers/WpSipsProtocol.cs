using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPS.ISO20022.Models.WpSips;

namespace SIPS.ISO20022.Helpers;

public sealed record ProtocolDiagnostic(string Stage, string Code, string? Path, string Detail);

public sealed class WpSipsValidationException(IReadOnlyList<ProtocolDiagnostic> diagnostics)
    : Exception(string.Join("; ", diagnostics.Select(x => $"{x.Stage}:{x.Code}:{x.Detail}")))
{
    public IReadOnlyList<ProtocolDiagnostic> Diagnostics { get; } = diagnostics;
}

public static class WpSipsXml
{
    internal static readonly XNamespace H = WpSipsNamespaces.Header;
    internal static readonly XNamespace D009 = WpSipsNamespaces.Admi009;
    internal static readonly XNamespace D010 = WpSipsNamespaces.Admi010;
    internal static readonly XNamespace D002 = WpSipsNamespaces.Admi002;

    public static XDocument ParseSecure(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw new WpSipsValidationException([new("Envelope", "EMPTY", null, "Message is required.")]);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 };
        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    internal static XElement Header(BusinessHeader h)
    {
        Required(h.From, nameof(h.From)); Required(h.To, nameof(h.To)); Required(h.BusinessMessageId, nameof(h.BusinessMessageId));
        Required(h.MessageDefinitionId, nameof(h.MessageDefinitionId)); Required(h.BusinessService, nameof(h.BusinessService));
        if (h.BusinessMessageId.Length > 35) throw new ArgumentOutOfRangeException(nameof(h.BusinessMessageId));
        XElement Party(string value) => new(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "Othr", new XElement(H + "Id", value))));
        var app = new XElement(H + "AppHdr",
            new XElement(H + "Fr", Party(h.From)), new XElement(H + "To", Party(h.To)),
            new XElement(H + "BizMsgIdr", h.BusinessMessageId), new XElement(H + "MsgDefIdr", h.MessageDefinitionId),
            new XElement(H + "BizSvc", h.BusinessService), new XElement(H + "CreDt", h.CreatedAt.ToUniversalTime().ToString("O")));
        if (h.RelatedBusinessMessageId is { } related)
            app.Add(new XElement(H + "Rltd", new XElement(H + "Fr", Party(h.To)), new XElement(H + "To", Party(h.From)),
                new XElement(H + "BizMsgIdr", related), new XElement(H + "MsgDefIdr", h.RelatedMessageDefinitionId ?? WpSipsMessageTypes.StaticDataRequest),
                string.IsNullOrWhiteSpace(h.RelatedBusinessService ?? h.BusinessService) ? null : new XElement(H + "BizSvc", h.RelatedBusinessService ?? h.BusinessService),
                new XElement(H + "CreDt", (h.RelatedCreatedAt ?? h.CreatedAt).ToUniversalTime().ToString("O"))));
        return app;
    }

    internal static XDocument Envelope(BusinessHeader h, XElement document)
        => new(new XElement("BusinessLayer", new XAttribute("Id", "BL-" + h.BusinessMessageId),
            new XAttribute(XNamespace.Xmlns + "header", H), new XAttribute(XNamespace.Xmlns + "document", document.Name.Namespace), Header(h), document));

    internal static void Required(string? value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Required.", name); }
    internal static string Decimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    internal static decimal Number(XElement e) => decimal.Parse(e.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
}

public static class WpSipsInformationMessageBuilder
{
    private const string RequestPlace = "/Document/StatcDataReq/DataReqDtls";
    private const string ReportPlace = "/Document/StatcDataRpt/RptDtls";

    public static string BuildFxRequest(BusinessHeader header, string serviceRequestId, FxRateRequest x)
    {
        ValidateRequestHeader(header, WpSipsProfiles.Fx);
        if (x.Amount <= 0 || x.IsInvoice != (x.InvoiceCurrency is not null) || x.InvoiceCurrency is not null and not ("USD" or "CURR")) throw new ArgumentException("Invalid invoice/amount combination.", nameof(x));
        XNamespace n = WpSipsNamespaces.FxRequest;
        return Request(header, serviceRequestId, new XElement(n + "FxRateRequest",
            new XElement(n + "SenderCountry", x.SenderCountry), new XElement(n + "ReceiverCountry", x.ReceiverCountry),
            new XElement(n + "SenderCurrency", x.SenderCurrency), new XElement(n + "ReceiverCurrency", x.ReceiverCurrency),
            new XElement(n + "ReceiverBank", x.ReceiverBank), new XElement(n + "LocalInstrument", x.LocalInstrument),
            new XElement(n + "Amount", WpSipsXml.Decimal(x.Amount)), new XElement(n + "IsInvoice", x.IsInvoice),
            x.InvoiceCurrency is null ? null : new XElement(n + "InvoiceCurrency", x.InvoiceCurrency)));
    }

    public static string BuildParticipantRequest(BusinessHeader header, string serviceRequestId, ParticipantDiscoveryRequest x)
    {
        ValidateRequestHeader(header, WpSipsProfiles.Participant); XNamespace n = WpSipsNamespaces.ParticipantRequest;
        var modes = (x.Bic is null ? 0 : 1) + (x.PapssId is null ? 0 : 1) + (x.Bic is null && x.PapssId is null ? 1 : 0);
        if (modes != 1 || ((x.Bic is not null || x.PapssId is not null) && (x.Online is not null || x.Type is not null))) throw new ArgumentException("Exactly one discovery mode is required.", nameof(x));
        XElement query = x.Bic is not null ? new(n + "ByBic", x.Bic) : x.PapssId is not null ? new(n + "ByPapssId", x.PapssId)
            : new(n + "List", x.Online is null ? null : new XElement(n + "Online", x.Online), x.Type is null ? null : new XElement(n + "Type", x.Type));
        return Request(header, serviceRequestId, new XElement(n + "ParticipantDiscoveryRequest", query));
    }

    public static string BuildReadinessRequest(BusinessHeader header, string serviceRequestId, ReadinessRequest x)
    {
        ValidateRequestHeader(header, WpSipsProfiles.Readiness); XNamespace n = WpSipsNamespaces.ReadinessRequest;
        if ((x.Bic is null) == (x.PapssId is null)) throw new ArgumentException("Exactly one participant identity is required.", nameof(x));
        return Request(header, serviceRequestId, new XElement(n + "ParticipantReadinessRequest", x.PapssId is not null ? new XElement(n + "PapssId", x.PapssId) : new XElement(n + "Bic", x.Bic)));
    }

    public static string BuildFxResponse(BusinessHeader header, string responseId, string requestId, FxRateResponse x)
    {
        ValidateResponseHeader(header, WpSipsProfiles.Fx); XNamespace n = WpSipsNamespaces.FxResponse;
        XElement payload;
        if (x.Error is { } error) payload = new(n + "FxRateResponse", Error(n, error));
        else
        {
            if (x.Rates.Count == 0) throw new ArgumentException("At least one PAPSS rate is required.", nameof(x));
            payload = new(n + "FxRateResponse", x.Rates.Select(r => new XElement(n + "Rate", new XAttribute("type", r.Type), r.UpdateTime is null ? null : new XAttribute("updateTime", r.UpdateTime.Value.ToString("O")), WpSipsXml.Decimal(r.Value))),
                Amount(n, "SenderAmount", x.SenderAmount), Amount(n, "ExchangeAmount", x.ExchangeAmount), Amount(n, "ReceiverAmount", x.ReceiverAmount),
                x.NationalFeeAmount is null ? null : Amount(n, "NationalFeeAmount", x.NationalFeeAmount), x.FeeAmount is null ? null : Amount(n, "FeeAmount", x.FeeAmount));
        }
        return Report(header, responseId, requestId, payload);
    }

    public static string BuildParticipantResponse(BusinessHeader header, string responseId, string requestId, ParticipantDiscoveryResponse x)
    {
        ValidateResponseHeader(header, WpSipsProfiles.Participant); XNamespace n = WpSipsNamespaces.ParticipantResponse;
        var root = new XElement(n + "ParticipantDiscoveryResponse", x.Error is null ? x.Participants.Select(p => ParticipantElement(n, "Participant", p)) : [Error(n, x.Error)]);
        return Report(header, responseId, requestId, root);
    }

    public static string BuildReadinessResponse(BusinessHeader header, string responseId, string requestId, ReadinessResponse x)
    {
        ValidateResponseHeader(header, WpSipsProfiles.Readiness); XNamespace n = WpSipsNamespaces.ReadinessResponse;
        if ((x.Observation is null) == (x.Error is null)) throw new ArgumentException("Exactly one observation or error is required.", nameof(x));
        var root = new XElement(n + "ParticipantReadinessResponse", x.Error is null ? ReadinessElement(n, x.Observation!) : Error(n, x.Error));
        return Report(header, responseId, requestId, root);
    }

    private static string Request(BusinessHeader h, string id, XElement payload)
    {
        WpSipsXml.Required(id, nameof(id));
        var doc = new XElement(WpSipsXml.D009 + "Document", new XElement(WpSipsXml.D009 + "StatcDataReq",
            new XElement(WpSipsXml.D009 + "MsgId", id), new XElement(WpSipsXml.D009 + "DataReqDtls", new XElement(WpSipsXml.D009 + "Tp", h.BusinessService)),
            Supplement(WpSipsXml.D009, RequestPlace, payload)));
        return WpSipsXml.Envelope(h, doc).ToString(SaveOptions.DisableFormatting);
    }

    private static string Report(BusinessHeader h, string id, string requestId, XElement payload)
    {
        WpSipsXml.Required(id, nameof(id)); WpSipsXml.Required(requestId, nameof(requestId));
        if (h.BusinessMessageId != id || h.RelatedBusinessMessageId is null) throw new ArgumentException("Response identifiers/header relation are required.", nameof(h));
        var doc = new XElement(WpSipsXml.D010 + "Document", new XElement(WpSipsXml.D010 + "StatcDataRpt",
            new XElement(WpSipsXml.D010 + "MsgId", id), new XElement(WpSipsXml.D010 + "RptDtls", new XElement(WpSipsXml.D010 + "Tp", h.BusinessService),
                new XElement(WpSipsXml.D010 + "ReqRef", requestId), new XElement(WpSipsXml.D010 + "RptKey", new XElement(WpSipsXml.D010 + "Key", requestId))),
            Supplement(WpSipsXml.D010, ReportPlace, payload)));
        return WpSipsXml.Envelope(h, doc).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Supplement(XNamespace iso, string place, XElement payload) => new(iso + "SplmtryData", new XElement(iso + "PlcAndNm", place), new XElement(iso + "Envlp", payload));
    private static XElement Amount(XNamespace n, string name, CurrencyAmount x) => new(n + name, new XAttribute("Ccy", x.Currency), WpSipsXml.Decimal(x.Value));
    private static XElement Error(XNamespace n, ServiceError x) => new(n + "Error", new XAttribute("authority", x.Authority), new XElement(n + "Code", x.Code), x.Description is null ? null : new XElement(n + "Description", x.Description));
    private static XElement ParticipantElement(XNamespace n, string name, Participant p) => new(n + name, new XElement(n + "PapssId", p.PapssId), p.Bic is null ? null : new XElement(n + "Bic", p.Bic), new XElement(n + "Name", p.Name),
        new XElement(n + "CountryCode", p.CountryCode), new XElement(n + "Status", p.Status), p.PaymentSchemas.Select(x => new XElement(n + "PaymentSchema", x)), p.Currencies.Select(x => new XElement(n + "Currency", x)), new XElement(n + "Online", p.Online), new XElement(n + "NonInstant", p.NonInstant));
    private static XElement ReadinessElement(XNamespace n, Participant p) => new(n + "Observation", new XElement(n + "PapssId", p.PapssId), p.Bic is null ? null : new XElement(n + "Bic", p.Bic), new XElement(n + "Status", p.Status), new XElement(n + "Online", p.Online), new XElement(n + "NonInstant", p.NonInstant), p.Currencies.Select(x => new XElement(n + "Currency", x)), p.PaymentSchemas.Select(x => new XElement(n + "PaymentSchema", x)));
    private static void ValidateRequestHeader(BusinessHeader h, string profile) { if (h.MessageDefinitionId != WpSipsMessageTypes.StaticDataRequest || h.BusinessService != profile || h.RelatedBusinessMessageId is not null) throw new ArgumentException("Invalid request header.", nameof(h)); }
    private static void ValidateResponseHeader(BusinessHeader h, string profile) { if (h.MessageDefinitionId != WpSipsMessageTypes.StaticDataReport || h.BusinessService != profile || h.RelatedBusinessMessageId is null) throw new ArgumentException("Invalid response header.", nameof(h)); }
}

public static class WpSipsProtocolValidator
{
    private static readonly Assembly Assembly = typeof(WpSipsProtocolValidator).Assembly;
    public static void Validate(string xml)
    {
        var doc = WpSipsXml.ParseSecure(xml); var errors = new List<ProtocolDiagnostic>();
        var root = doc.Root;
        if (root?.Name.LocalName != "BusinessLayer" || root.Name.NamespaceName.Length != 0 || root.Attribute("Id") is null)
            throw new WpSipsValidationException([new("Envelope", "BUSINESS_LAYER", "/BusinessLayer", "One identified unqualified BusinessLayer is required.")]);
        var children = root.Elements().ToArray();
        if (children.Length != 2 || children[0].Name != WpSipsXml.H + "AppHdr" || children[1].Name.LocalName != "Document")
            throw new WpSipsValidationException([new("Envelope", "CHILDREN", "/BusinessLayer", "AppHdr then Document required.")]);
        ValidateElement(children[0], WpSipsNamespaces.Header, "head.001.001.03.xsd", "BAH", errors);
        var message = (string?)children[0].Element(WpSipsXml.H + "MsgDefIdr");
        var service = (string?)children[0].Element(WpSipsXml.H + "BizSvc");
        var expectedNs = message switch { WpSipsMessageTypes.StaticDataRequest => WpSipsNamespaces.Admi009, WpSipsMessageTypes.StaticDataReport => WpSipsNamespaces.Admi010, WpSipsMessageTypes.MessageReject => WpSipsNamespaces.Admi002, _ => null };
        if (expectedNs is null || children[1].Name.NamespaceName != expectedNs) errors.Add(new("Consistency", "MESSAGE_NAMESPACE", "/BusinessLayer/Document", "MsgDefIdr/document mismatch."));
        else ValidateElement(children[1], expectedNs, message + ".xsd", "Document", errors);
        if (message is WpSipsMessageTypes.StaticDataRequest or WpSipsMessageTypes.StaticDataReport)
        {
            if (!WpSipsProfiles.All.Contains(service ?? "")) errors.Add(new("Profile", "UNKNOWN", "/BusinessLayer/AppHdr/BizSvc", "Unknown profile."));
            var type = children[1].Descendants().FirstOrDefault(x => x.Name.LocalName == "Tp")?.Value;
            if (!StringComparer.Ordinal.Equals(service, type)) errors.Add(new("Consistency", "PROFILE_TYPE", null, "BizSvc/Tp mismatch."));
            var envelopes = children[1].Descendants().Where(x => x.Name.LocalName == "Envlp" && x.Parent?.Name.LocalName == "SplmtryData").ToArray();
            var expectedPlace = message == WpSipsMessageTypes.StaticDataRequest ? "/Document/StatcDataReq/DataReqDtls" : "/Document/StatcDataRpt/RptDtls";
            var place = children[1].Descendants().FirstOrDefault(x => x.Name.LocalName == "PlcAndNm")?.Value;
            if (place != expectedPlace) errors.Add(new("Consistency", "PLACE_AND_NAME", null, "PlcAndNm is not the frozen profile location."));
            if (envelopes.Length != 1 || envelopes[0].Elements().Count() != 1) errors.Add(new("Extension", "CARDINALITY", null, "Exactly one extension root is required."));
            else
            {
                var ext = envelopes[0].Elements().Single(); var (ns, schema) = Extension(service!, message!);
                if (ext.Name.NamespaceName != ns) errors.Add(new("Extension", "NAMESPACE", null, "Profile extension namespace mismatch."));
                else ValidateElement(ext, ns, schema, "Extension", errors);
            }
            var bizId = children[0].Element(WpSipsXml.H + "BizMsgIdr")?.Value;
            var docId = children[1].Descendants().FirstOrDefault(x => x.Name.LocalName == "MsgId")?.Value;
            if (message == WpSipsMessageTypes.StaticDataReport)
            {
                var related = children[0].Element(WpSipsXml.H + "Rltd")?.Element(WpSipsXml.H + "BizMsgIdr")?.Value;
                var reqRef = children[1].Descendants().FirstOrDefault(x => x.Name.LocalName == "ReqRef")?.Value;
                var key = children[1].Descendants().FirstOrDefault(x => x.Name.LocalName == "RptKey")?.Elements().FirstOrDefault(x => x.Name.LocalName == "Key")?.Value;
                if (bizId != docId) errors.Add(new("Correlation", "RESPONSE_MESSAGE_ID", null, "BizMsgIdr must equal report MsgId."));
                if (string.IsNullOrWhiteSpace(related) || string.IsNullOrWhiteSpace(reqRef) || reqRef != key) errors.Add(new("Correlation", "REQUEST_REFERENCE", null, "Rltd must exist and ReqRef must equal RptKey/Key."));
            }
            if (root.DescendantsAndSelf().Attributes("Id").GroupBy(x => x.Value, StringComparer.Ordinal).Any(x => x.Count() != 1)) errors.Add(new("Consistency", "DUPLICATE_ID", null, "Id values must be unique."));
        }
        else if (message == WpSipsMessageTypes.MessageReject)
        {
            var related = children[0].Element(WpSipsXml.H + "Rltd")?.Element(WpSipsXml.H + "BizMsgIdr")?.Value;
            var rejected = children[1].Descendants().FirstOrDefault(x => x.Name.LocalName == "Ref")?.Value;
            if (string.IsNullOrWhiteSpace(related) || related != rejected) errors.Add(new("Correlation", "REJECTED_REFERENCE", null, "admi.002 Rltd and RltdRef/Ref must identify the same rejected BAH."));
            if (root.DescendantsAndSelf().Attributes("Id").GroupBy(x => x.Value, StringComparer.Ordinal).Any(x => x.Count() != 1)) errors.Add(new("Consistency", "DUPLICATE_ID", null, "Id values must be unique."));
        }
        if (errors.Count != 0) throw new WpSipsValidationException(errors);
    }

    public static void ValidateCorrelation(string requestXml, string responseXml)
    {
        Validate(requestXml); Validate(responseXml);
        var request = WpSipsInformationMessageParser.Parse(requestXml); var response = WpSipsInformationMessageParser.Parse(responseXml);
        var errors = new List<ProtocolDiagnostic>();
        if (request.Header.MessageDefinitionId != WpSipsMessageTypes.StaticDataRequest || response.Header.MessageDefinitionId is not (WpSipsMessageTypes.StaticDataReport or WpSipsMessageTypes.MessageReject)) errors.Add(new("Correlation","MESSAGE_PAIR",null,"admi.009 with admi.010 or admi.002 response required."));
        if (request.Header.BusinessMessageId != response.Header.RelatedBusinessMessageId) errors.Add(new("Correlation","BAH_RELATED",null,"Response Rltd must identify request BizMsgIdr."));
        if (response.Header.MessageDefinitionId == WpSipsMessageTypes.StaticDataReport && request.ServiceMessageId != response.RequestReference) errors.Add(new("Correlation","SERVICE_REFERENCE",null,"Response ReqRef must identify request MsgId."));
        if (request.Header.BusinessService != response.Header.BusinessService || request.Header.From != response.Header.To || request.Header.To != response.Header.From) errors.Add(new("Correlation","PROFILE_PARTIES",null,"Profile and reversed parties must agree."));
        if (errors.Count != 0) throw new WpSipsValidationException(errors);
    }

    private static (string Namespace, string Schema) Extension(string profile, string message) => (profile, message) switch
    {
        (WpSipsProfiles.Fx, WpSipsMessageTypes.StaticDataRequest) => (WpSipsNamespaces.FxRequest, "SPS.PAPSS.FX.001.request.xsd"),
        (WpSipsProfiles.Fx, _) => (WpSipsNamespaces.FxResponse, "SPS.PAPSS.FX.001.response.xsd"),
        (WpSipsProfiles.Participant, WpSipsMessageTypes.StaticDataRequest) => (WpSipsNamespaces.ParticipantRequest, "SPS.PAPSS.PARTICIPANT.001.request.xsd"),
        (WpSipsProfiles.Participant, _) => (WpSipsNamespaces.ParticipantResponse, "SPS.PAPSS.PARTICIPANT.001.response.xsd"),
        (WpSipsProfiles.Readiness, WpSipsMessageTypes.StaticDataRequest) => (WpSipsNamespaces.ReadinessRequest, "SPS.PAPSS.READINESS.001.request.xsd"),
        _ => (WpSipsNamespaces.ReadinessResponse, "SPS.PAPSS.READINESS.001.response.xsd")
    };

    private static void ValidateElement(XElement element, string ns, string file, string stage, List<ProtocolDiagnostic> errors)
    {
        try
        {
            var set = new XmlSchemaSet { XmlResolver = null }; using var stream = Resource(file); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            set.Add(ns, reader); set.Compile(); var wrapper = new XDocument(new XElement(element));
            wrapper.Validate(set, (_, e) => errors.Add(new(stage, "XSD", null, e.Message)), true);
        }
        catch (Exception ex) { errors.Add(new(stage, "SCHEMA", null, ex.GetType().Name)); }
    }

    private static Stream Resource(string suffix)
    {
        var name = Assembly.GetManifestResourceNames().SingleOrDefault(x => x.EndsWith(suffix, StringComparison.Ordinal)) ?? throw new InvalidOperationException("Missing schema resource: " + suffix);
        return Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing schema stream: " + suffix);
    }
}

public static class WpSipsInformationMessageParser
{
    public static WpSipsMessage<object> Parse(string xml)
    {
        WpSipsProtocolValidator.Validate(xml); var doc = WpSipsXml.ParseSecure(xml); var root = doc.Root!; var app = root.Element(WpSipsXml.H + "AppHdr")!;
        var document = root.Elements().Single(x => x.Name.LocalName == "Document"); var def = app.Element(WpSipsXml.H + "MsgDefIdr")!.Value;
        var serviceId = def == WpSipsMessageTypes.MessageReject ? app.Element(WpSipsXml.H + "BizMsgIdr")!.Value : document.Descendants().First(x => x.Name.LocalName == "MsgId").Value;
        var requestRef = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "ReqRef")?.Value;
        var payload = def == WpSipsMessageTypes.MessageReject ? document.Descendants().First(x => x.Name.LocalName == "admi.002.001.01") : document.Descendants().First(x => x.Name.LocalName == "Envlp").Elements().Single();
        var header = new BusinessHeader(Party(app, "Fr"), Party(app, "To"), app.Element(WpSipsXml.H + "BizMsgIdr")!.Value, def,
            app.Element(WpSipsXml.H + "BizSvc")?.Value ?? "", DateTimeOffset.Parse(app.Element(WpSipsXml.H + "CreDt")!.Value, CultureInfo.InvariantCulture), app.Element(WpSipsXml.H + "Rltd")?.Element(WpSipsXml.H + "BizMsgIdr")?.Value);
        return new(header, serviceId, requestRef, ParsePayload(payload));
    }

    private static string Party(XElement app, string name) => app.Element(WpSipsXml.H + name)!.Descendants(WpSipsXml.H + "Id").Single().Value;
    private static object ParsePayload(XElement p)
    {
        var n = p.Name.Namespace;
        if (p.Name.LocalName == "admi.002.001.01") { var reason=p.Element(n+"Rsn")!; return new AdminReject(p.Element(n+"RltdRef")!.Element(n+"Ref")!.Value,V(reason,"RjctgPtyRsn"),DateTimeOffset.Parse(V(reason,"RjctnDtTm"),CultureInfo.InvariantCulture),reason.Element(n+"ErrLctn")?.Value,reason.Element(n+"RsnDesc")?.Value); }
        if (p.Name.LocalName == "FxRateRequest") return new FxRateRequest(V(p,"SenderCountry"),V(p,"ReceiverCountry"),V(p,"SenderCurrency"),V(p,"ReceiverCurrency"),V(p,"ReceiverBank"),V(p,"LocalInstrument"),WpSipsXml.Number(p.Element(n+"Amount")!),bool.Parse(V(p,"IsInvoice")),p.Element(n+"InvoiceCurrency")?.Value);
        if (p.Name.LocalName == "ParticipantDiscoveryRequest") { var q=p.Elements().Single(); return new ParticipantDiscoveryRequest(q.Element(n+"Online") is{}o?bool.Parse(o.Value):null,q.Element(n+"Type")?.Value,q.Name.LocalName=="ByBic"?q.Value:null,q.Name.LocalName=="ByPapssId"?q.Value:null); }
        if (p.Name.LocalName == "ParticipantReadinessRequest") return new ReadinessRequest(p.Element(n+"PapssId")?.Value,p.Element(n+"Bic")?.Value);
        if (p.Name.LocalName == "FxRateResponse")
        {
            if (p.Element(n+"Error") is {} e) return new FxRateResponse([],new(0,"XXX"),new(0,"XXX"),new(0,"XXX"),null,null,Err(e));
            CurrencyAmount A(string name){var a=p.Element(n+name)!;return new(WpSipsXml.Number(a),(string)a.Attribute("Ccy")!);}
            return new FxRateResponse(p.Elements(n+"Rate").Select(r=>new FxRate(WpSipsXml.Number(r),(string)r.Attribute("type")!,r.Attribute("updateTime") is{}u?DateTimeOffset.Parse(u.Value,CultureInfo.InvariantCulture):null)).ToArray(),A("SenderAmount"),A("ExchangeAmount"),A("ReceiverAmount"),p.Element(n+"NationalFeeAmount") is null?null:A("NationalFeeAmount"),p.Element(n+"FeeAmount") is null?null:A("FeeAmount"));
        }
        if (p.Name.LocalName == "ParticipantDiscoveryResponse") return p.Element(n+"Error") is{} de ? new ParticipantDiscoveryResponse([],Err(de)) : new ParticipantDiscoveryResponse(p.Elements(n+"Participant").Select(Participant).ToArray());
        if (p.Name.LocalName == "ParticipantReadinessResponse") return p.Element(n+"Error") is{} re ? new ReadinessResponse(null,Err(re)) : new ReadinessResponse(Participant(p.Element(n+"Observation")!),null);
        return p;
    }
    private static ServiceError Err(XElement e)=>new((string)e.Attribute("authority")!,V(e,"Code"),e.Element(e.Name.Namespace+"Description")?.Value);
    private static Participant Participant(XElement p){var n=p.Name.Namespace;return new(V(p,"PapssId"),p.Element(n+"Bic")?.Value,p.Element(n+"Name")?.Value??"",p.Element(n+"CountryCode")?.Value??"",V(p,"Status"),p.Elements(n+"PaymentSchema").Select(x=>x.Value).ToArray(),p.Elements(n+"Currency").Select(x=>x.Value).ToArray(),bool.Parse(V(p,"Online")),bool.Parse(V(p,"NonInstant")));}
    private static string V(XElement p,string name)=>p.Element(p.Name.Namespace+name)!.Value;
}
