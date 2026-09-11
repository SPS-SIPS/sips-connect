using System.Net;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SIPS.ISO20022.Options;

namespace SIPS.ISO20022.Helpers;
public static class Transformers
{
    public static string GeneratePrefixedXml(XElement envelopeElement, string docNS)
    {
        // Add namespace declarations to the root element
        envelopeElement.SetAttributeValue(XNamespace.Xmlns + "header", "urn:iso:std:iso:20022:tech:xsd:head.001.001.03");
        envelopeElement.SetAttributeValue(XNamespace.Xmlns + "document", docNS);

        // Get namespaces for header and document
        XNamespace headerNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
        XNamespace documentNamespace = docNS;

        // Find and update AppHdr and Document elements to have explicit prefixes
        var appHdr = envelopeElement.Element("{urn:iso:std:iso:20022:tech:xsd:head.001.001.03}AppHdr");
        if (appHdr != null)
        {
            appHdr.Name = headerNamespace + "AppHdr"; // Update the element name with the prefixed namespace
            var businessMessageId = appHdr.Element(headerNamespace + "BizMsgIdr")?.Value;
            if (string.IsNullOrWhiteSpace(businessMessageId)) throw new InvalidOperationException("BizMsgIdr is required before rendering an identified BusinessLayer.");
            envelopeElement.SetAttributeValue("Id", "BL-" + businessMessageId);
        }

        var document = envelopeElement.Element("{urn:iso:std:iso:20022:tech:xsd:acmt.023.001.02}Document");
        if (document != null)
        {
            document.Name = documentNamespace + "Document"; // Update the element name with the prefixed namespace
        }

        if (docNS.Contains("pacs.002", StringComparison.OrdinalIgnoreCase))
        {
            // Keep response acceptance timestamps in explicit UTC form, e.g. 2026-06-11T12:30:19.750Z.
            UpdateDateTimeElements(envelopeElement, "AccptncDtTm", "yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        }
        else
        {
            // Preserve the legacy request format accepted by IPS for pacs.008 AccptncDtTm.
            UpdateDateTimeElements(envelopeElement, "AccptncDtTm", "yyyy-MM-dd'T'HH:mm:ss.fffzzz");
        }


        // Serialize the XElement with prefixes
        using var stringWriter = new StringWriter();
        using var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
            NewLineOnAttributes = true
        });

        envelopeElement.Save(writer);
        writer.Flush();

        return stringWriter.ToString();
    }

    public static string GenerateId(string prefix)
    {
        var today = DateTimeOffset.UtcNow;
        var yearSuffix = today.Year.ToString()[3];
        var dayOfYear = today.DayOfYear;
        var hour = today.Hour.ToString("D2");
        var ticks = today.Ticks.ToString("D8");

        return $"{prefix}{yearSuffix}{dayOfYear}{hour}{ticks}";
    }

    public static string GetMessageType(string coreBankingResponse)
    {
        try
        {
            var document = XDocument.Parse(coreBankingResponse);
            var appHeader = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "AppHdr");
            return appHeader?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "MsgDefIdr")
                ?.Value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string? GetCallBackEndpoint(string MsgDefIdr, ISO20022Options links)
    {
        return MsgDefIdr switch
        {
            "acmt.023.001.03" => links.Verification,
            "pacs.008.001.10" => links.Transfer,
            "pacs.028.001.05" => links.Status,
            "pacs.004.001.11" => links.Return,
            _ => null
        };
    }

    private static void UpdateDateTimeElements(XElement element, string elementName, string dateFormat)
    {
        foreach (var dateElement in element.DescendantsAndSelf().Where(e =>
                    e.Name.LocalName == elementName && DateTimeOffset.TryParse(e.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)))
        {
            if (DateTimeOffset.TryParse(dateElement.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateValue))
            {
                dateElement.Value = dateValue
                    .ToUniversalTime()
                    .ToString(dateFormat, CultureInfo.InvariantCulture);
            }
        }
    }

    public static string TransformSIPSHttpError(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.BadRequest => "The SIPS API could not process the request please check your schema.",
            HttpStatusCode.Unauthorized => "The SIPS API Could not authenticate the request.",
            HttpStatusCode.Forbidden => "The SIPS API has refused the request.",
            HttpStatusCode.NotFound => "The SIPS API could not find the requested resource.",
            HttpStatusCode.InternalServerError => "The SIPS API has encountered an error processing the request, please contact the SIPS IT team.",
            _ => "The SIPS API has encountered an error processing the request, please contact the SIPS IT team.",
        };
    }

    public static string TransformCoreBankHttpError(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.BadRequest => "The Core bank API could not process the request please check your schema.",
            HttpStatusCode.Unauthorized => "The Core bank API Could not authenticate the request.",
            HttpStatusCode.Forbidden => "The Core bank API has refused the request.",
            HttpStatusCode.NotFound => "The Core bank API could not find the requested resource.",
            HttpStatusCode.InternalServerError => "The Core bank API has encountered an error processing the request, please contact the Core bank IT team.",
            _ => "The Core bank API has encountered an error processing the request, please contact the Core bank IT team.",
        };
    }
}
