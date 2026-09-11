using System.Xml.Linq;
using SIPS.ISO20022.Models.WpSips;

namespace SIPS.ISO20022.Helpers;

public static class AdminMessageBuilder
{
    public static string BuildForRejectedEnvelope(string rejectedXml, string responseBusinessMessageId, DateTimeOffset rejectedAt,
        string reasonCode, string? errorLocation = null, string? description = null)
    {
        WpSipsXml.Required(responseBusinessMessageId, nameof(responseBusinessMessageId));
        var document = WpSipsXml.ParseSecure(rejectedXml);
        var app = document.Root?.Element(WpSipsXml.H + "AppHdr") ?? throw new WpSipsValidationException([new("BAH", "MISSING", null, "A safely parsed AppHdr is required before emitting admi.002.")]);
        static string Party(XElement app, XName name) => app.Element(name)?.Descendants(WpSipsXml.H + "Id").SingleOrDefault()?.Value ?? throw new WpSipsValidationException([new("BAH", "PARTY", null, "A unique party identifier is required.")]);
        var rejectedId = app.Element(WpSipsXml.H + "BizMsgIdr")?.Value;
        var rejectedDefinition = app.Element(WpSipsXml.H + "MsgDefIdr")?.Value;
        var rejectedCreatedAt = DateTimeOffset.Parse(app.Element(WpSipsXml.H + "CreDt")?.Value ?? throw new WpSipsValidationException([new("BAH", "CREATION_TIME", null, "Rejected BAH creation time is required.")]));
        var service = app.Element(WpSipsXml.H + "BizSvc")?.Value;
        WpSipsXml.Required(rejectedId, nameof(rejectedId));
        if (!string.IsNullOrWhiteSpace(service))
        {
            var header = new BusinessHeader(Party(app, WpSipsXml.H + "To"), Party(app, WpSipsXml.H + "Fr"), responseBusinessMessageId,
                "admi.002.001.01", service!, rejectedAt, rejectedId, rejectedDefinition, service, rejectedCreatedAt);
            return Build(header, rejectedId!, reasonCode, rejectedAt, errorLocation, description);
        }
        var n=WpSipsXml.D002;XElement PartyElement(string value)=>new(WpSipsXml.H+"FIId",new XElement(WpSipsXml.H+"FinInstnId",new XElement(WpSipsXml.H+"Othr",new XElement(WpSipsXml.H+"Id",value))));
        var responseHeader=new XElement(WpSipsXml.H+"AppHdr",new XElement(WpSipsXml.H+"Fr",PartyElement(Party(app,WpSipsXml.H+"To"))),new XElement(WpSipsXml.H+"To",PartyElement(Party(app,WpSipsXml.H+"Fr"))),new XElement(WpSipsXml.H+"BizMsgIdr",responseBusinessMessageId),new XElement(WpSipsXml.H+"MsgDefIdr","admi.002.001.01"),new XElement(WpSipsXml.H+"CreDt",rejectedAt.ToUniversalTime().ToString("O")),new XElement(WpSipsXml.H+"Rltd",app.Elements().Where(x=>x.Name.LocalName is not ("Rltd" or "Sgntr"))));
        var rejectDocument=new XElement(n+"Document",new XElement(n+"admi.002.001.01",new XElement(n+"RltdRef",new XElement(n+"Ref",rejectedId)),new XElement(n+"Rsn",new XElement(n+"RjctgPtyRsn",reasonCode),new XElement(n+"RjctnDtTm",rejectedAt.ToUniversalTime().ToString("O")),errorLocation is null?null:new XElement(n+"ErrLctn",errorLocation),description is null?null:new XElement(n+"RsnDesc",description))));
        var xml=new XDocument(new XElement("BusinessLayer",new XAttribute("Id","BL-"+responseBusinessMessageId),new XAttribute(XNamespace.Xmlns+"header",WpSipsXml.H),new XAttribute(XNamespace.Xmlns+"document",n),responseHeader,rejectDocument)).ToString(SaveOptions.DisableFormatting);WpSipsProtocolValidator.Validate(xml);return xml;
    }

    public static string Build(BusinessHeader header, string rejectedBusinessMessageId, string reasonCode,
        DateTimeOffset rejectedAt, string? errorLocation = null, string? description = null)
    {
        WpSipsXml.Required(rejectedBusinessMessageId, nameof(rejectedBusinessMessageId));
        WpSipsXml.Required(reasonCode, nameof(reasonCode));
        if (header.MessageDefinitionId != "admi.002.001.01" || header.RelatedBusinessMessageId != rejectedBusinessMessageId)
            throw new ArgumentException("The rejection BAH must identify and relate to the rejected message.", nameof(header));
        if (reasonCode.Length > 35 || errorLocation?.Length > 350 || description?.Length > 350) throw new ArgumentOutOfRangeException(nameof(reasonCode));
        var n = WpSipsXml.D002;
        var document = new XElement(n + "Document", new XElement(n + "admi.002.001.01",
            new XElement(n + "RltdRef", new XElement(n + "Ref", rejectedBusinessMessageId)),
            new XElement(n + "Rsn", new XElement(n + "RjctgPtyRsn", reasonCode), new XElement(n + "RjctnDtTm", rejectedAt.ToUniversalTime().ToString("O")),
                errorLocation is null ? null : new XElement(n + "ErrLctn", errorLocation), description is null ? null : new XElement(n + "RsnDesc", description))));
        var xml = WpSipsXml.Envelope(header, document).ToString(SaveOptions.DisableFormatting);
        WpSipsProtocolValidator.Validate(xml);
        return xml;
    }
}

public static class AdminRejectReasonCodes
{
    public const string InvalidXml = "INVALID_XML";
    public const string SignatureInvalid = "SIGNATURE_INVALID";
    public const string MandatoryElementMissing = "MANDATORY_MISSING";
    public const string DuplicateMessageConflict = "DUPLICATE_CONFLICT";
    public const string DuplicateMessageInProcess = "DUPLICATE_IN_PROCESS";
    public const string TechnicalError = "TECHNICAL_ERROR";
}
