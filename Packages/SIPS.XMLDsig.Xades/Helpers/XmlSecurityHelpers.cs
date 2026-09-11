using System.Xml;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace SIPS.XMLDsig.Xades.Helpers;

public static class XmlSecurityHelpers
{
    public const string XadesNamespaceUri = "http://uri.etsi.org/01903/v1.3.2#";
    public const string XadesPrefix = "xades";
    public static string GetDocumentNamespace(XmlDocument envelope, string name = "document") => envelope.DocumentElement!.GetNamespaceOfPrefix(name);

    public static XmlElement GetFirstOfXmlElementsByTagWithPrefix(XmlElement e, string tag, string? ns = null)
    {
        var res = GetFirstOfXmlElementsByTagOrNull(e, tag, ns) ?? GetFirstOfXmlElementsByTagOrNull(e, $"{e.Prefix}:{tag}", ns) ?? throw new InvalidOperationException($"Tag '{tag}' in ns '{ns}' isn't found.");
        return res;
    }

    public static XmlElement? GetFirstOfXmlElementsByTagOrNull(XmlElement e, string tag, string? ns = null)
    {
        if (e != null)
        {
            XmlNodeList? nodeList;
            if (ns == null)
                nodeList = e.GetElementsByTagName(tag);
            else
                nodeList = e.GetElementsByTagName(tag, ns);
            if ((nodeList?.Count ?? 0) < 1)
                return null;
            return nodeList != null ? (XmlElement?)nodeList[0] : null;
        }
        throw new ArgumentNullException(nameof(e));
    }


    public static XmlDocument GetAsXmlDocument(string xml)
    {
        var doc = new XmlDocument
        {
            PreserveWhitespace = true
        };
        doc.LoadXml(xml);
        return doc;
    }

    public static string FormatIssuerDN(X509Certificate certificate)
    {
        var dnComponents = new Dictionary<DerObjectIdentifier, List<string>>();

        var oids = certificate.IssuerDN.GetOidList();
        var values = certificate.IssuerDN.GetValueList();

        for (int i = 0; i < oids.Count; i++)
        {
            var identifier = oids[i];
            var value = values[i].ToString();

            if (!dnComponents.ContainsKey(identifier))
            {
                dnComponents[identifier] = [];
            }
            dnComponents[identifier].Add(value);
        }

        var orderedComponents = new List<string>();

        if (dnComponents.ContainsKey(X509Name.CN))
        {
            orderedComponents.AddRange(dnComponents[X509Name.CN].Select(cn => $"CN={cn}"));
        }

        if (dnComponents.ContainsKey(X509Name.DC))
        {
            orderedComponents.AddRange(dnComponents[X509Name.DC].OrderByDescending(x => x).Select(dc => $"DC={dc}"));
        }

        return string.Join(", ", orderedComponents);
    }

    public static bool CheckTransactionOwnerAgainstCertificate(XmlDocument envelope, string certificateOwner)
    {
        const string headerNs = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
        var appHeader = envelope.GetElementsByTagName("AppHdr", headerNs).Cast<XmlElement>().SingleOrDefault()
            ?? throw new InvalidOperationException("A unique AppHdr is required.");
        var from = appHeader.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().SingleOrDefault(x => x.LocalName == "Fr" && x.NamespaceURI == headerNs)
            ?? throw new InvalidOperationException("A unique top-level BAH From is required.");
        var fromId = from.GetElementsByTagName("Id", headerNs).Cast<XmlElement>().SingleOrDefault()
            ?? throw new InvalidOperationException("A unique BAH From identifier is required.");
        if (!StringComparer.Ordinal.Equals(fromId.InnerText.Trim(), certificateOwner.Trim())) return false;

        var ns = new XmlNamespaceManager(envelope.NameTable);
        ns.AddNamespace("document", GetDocumentNamespace(envelope, "document"));
        var messageType = ns.LookupNamespace("document") ?? throw new InvalidOperationException("Namespace 'document' not found.");

        if (messageType == "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.10")
        {
            var instgAgt = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "document:InstgAgt");
            return instgAgt.InnerText.Trim() == certificateOwner;
        }

        if (messageType == "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12")
        {
            var instdAgt = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "document:InstdAgt");
            return instdAgt.InnerText.Trim() == certificateOwner;
        }

        if (messageType == "urn:iso:std:iso:20022:tech:xsd:acmt.023.001.03")
        {
            var assgnr = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "document:Assgnr");
            return assgnr.InnerText.Trim() == certificateOwner;
        }

        if (messageType == "urn:iso:std:iso:20022:tech:xsd:acmt.024.001.03")
        {
            var assgne = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "document:Assgne");
            return assgne.InnerText.Trim() == certificateOwner;
        }

        if (messageType is "urn:iso:std:iso:20022:tech:xsd:admi.009.001.02" or "urn:iso:std:iso:20022:tech:xsd:admi.010.001.02" or "urn:iso:std:iso:20022:tech:xsd:admi.002.001.01" or "urn:iso:std:iso:20022:tech:xsd:pacs.028.001.05" or "urn:iso:std:iso:20022:tech:xsd:pacs.004.001.11")
        {
            return true;
        }

        return false;
    }


    public static bool CheckIfTheMessageAllowed(XmlDocument envelope, string BIC, bool isSigningType)
    {
        string[] allowedTypes = [
             "urn:iso:std:iso:20022:tech:xsd:acmt.023.001.03",
            "urn:iso:std:iso:20022:tech:xsd:acmt.024.001.03",
            "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.10",
            "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12"
        ];
        var ns = new XmlNamespaceManager(envelope.NameTable);
        ns.AddNamespace("document", GetDocumentNamespace(envelope, "document"));
        var messageType = ns.LookupNamespace("document") ?? throw new InvalidOperationException("Namespace 'document' not found.");

        if (!allowedTypes.Contains(messageType))
        {
            return false;
        }


        return messageType switch
        {
            "urn:iso:std:iso:20022:tech:xsd:acmt.023.001.03" => VerificationRequest(envelope.DocumentElement!, BIC, isSigningType),
            "urn:iso:std:iso:20022:tech:xsd:acmt.024.001.03" => VerificationResponse(envelope.DocumentElement!, BIC, isSigningType),
            "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.10" => TransferMessage(envelope.DocumentElement!, BIC, isSigningType),
            "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12" => TransferResponse(envelope.DocumentElement!, BIC, isSigningType),
            _ => false,
        };
    }

    private static bool TransferMessage(XmlElement doc, string allowedParticipant, bool isSigningType)
    {
        // get the descendent of the document:InstgAgt/document:FinInstnId/document:Othr/document:Id
        var InstgAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:InstgAgt");
        // get the descendent of the document:InstdAgt/document:FinInstnId/document:Othr/document:Id
        var InstdAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:InstdAgt");

        if (isSigningType)
        {
            return InstgAgt.InnerText.Trim() == allowedParticipant;
        }
        else
        {
            return InstdAgt.InnerText.Trim() == allowedParticipant;
        }
    }

    private static bool TransferResponse(XmlElement doc, string allowedParticipant, bool isSigningType)
    {
        // get the descendent of the document:InstgAgt/document:FinInstnId/document:Othr/document:Id
        var instgAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:InstgAgt");
        // get the descendent of the document:InstdAgt/document:FinInstnId/document:Othr/document:Id
        var instdAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:InstdAgt");

        if (isSigningType)
        {
            return instgAgt.InnerText.Trim() == allowedParticipant;
        }
        else
        {
            return instdAgt.InnerText.Trim() == allowedParticipant;
        }
    }

    private static bool VerificationRequest(XmlElement doc, string allowedParticipant, bool isSigningType)
    {
        // get the descendent of the document:Assgnr/document:Agt/document:FinInstnId/document:Othr/document:Id
        var instgAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:Assgnr");
        // get the descendent of the document:Assgne/document:Agt/document:FinInstnId/document:Othr/document:Id
        var instdAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:Assgne");

        if (isSigningType)
        {
            return instgAgt.InnerText.Trim() == allowedParticipant;
        }
        else
        {
            return instdAgt.InnerText.Trim() == allowedParticipant;
        }
    }

    private static bool VerificationResponse(XmlElement doc, string allowedParticipant, bool isSigningType)
    {
        // get the descendent of the document:Assgnr/document:Agt/document:FinInstnId/document:Othr/document:Id
        var instgAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:Assgnr");
        // get the descendent of the document:Assgne/document:Agt/document:FinInstnId/document:Othr/document:Id
        var instdAgt = GetFirstOfXmlElementsByTagWithPrefix(doc, "document:Assgne");

        if (isSigningType)
        {
            return instgAgt.InnerText.Trim() == allowedParticipant;
        }
        else
        {
            return instdAgt.InnerText.Trim() == allowedParticipant;
        }
    }
}

public enum AuthCheckResponse
{
    Forbidden = 1,
    NoAuthProvided,
    AuthIsOff,
    Authenticated
}
