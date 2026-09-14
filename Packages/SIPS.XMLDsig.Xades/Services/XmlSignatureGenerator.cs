using System.Security.Cryptography;
using System.Xml.Linq;
namespace SIPS.XMLDsig.Xades.Services;
public static class XmlSignatureGenerator
{
    public static XDocument GenerateSignatureXml(string keyInfoId, string signedPropsId, string businessLayerId, string certificateDigest, string x509IssuerName, string x509SerialNumber, string signingTime, string algorithm)
        => GenerateSignatureXml(keyInfoId, signedPropsId, businessLayerId, certificateDigest, x509IssuerName, x509SerialNumber, signingTime, algorithm, XadesProfile.IpsVendorLegacy);

    public static XDocument GenerateSignatureXml(string keyInfoId, string signedPropsId, string businessLayerId, string certificateDigest, string x509IssuerName, string x509SerialNumber, string signingTime, string algorithm, XadesProfile profile)
    {
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        XNamespace xades = "http://uri.etsi.org/01903/v1.3.2#";
        var signatureAlgorithm = SignatureAlgorithmURL(algorithm);
        if (profile == XadesProfile.IpsVendorLegacy)
            return GenerateLegacy(keyInfoId, signedPropsId, x509IssuerName, x509SerialNumber, signingTime, signatureAlgorithm);

        var signatureId = $"{signedPropsId}-signature";
        XDocument signatureDoc = new(
            new XElement(ds + "Signature", new XAttribute(XNamespace.Xmlns + "ds", ds), new XAttribute("Id", signatureId),
                new XElement(ds + "SignedInfo",
                    new XElement(ds + "CanonicalizationMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#")),
                    new XElement(ds + "SignatureMethod", new XAttribute("Algorithm", signatureAlgorithm)),
                    new XElement(ds + "Reference", new XAttribute("URI", $"#{keyInfoId}"),
                        new XElement(ds + "Transforms",
                            new XElement(ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#"))
                        ),
                        new XElement(ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                        new XElement(ds + "DigestValue", "digestValue1")
                    ),
                    new XElement(ds + "Reference", new XAttribute("URI", $"#{signedPropsId}-signedprops"),
                        new XAttribute("Type", "http://uri.etsi.org/01903/v1.3.2#SignedProperties"),
                        new XElement(ds + "Transforms",
                            new XElement(ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#"))
                        ),
                        new XElement(ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                        new XElement(ds + "DigestValue", "digestValue2")
                    ),
                    new XElement(ds + "Reference", new XAttribute("URI", $"#{businessLayerId}"),
                        new XElement(ds + "Transforms",
                            new XElement(ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature")),
                            new XElement(ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#"))
                        ),
                        new XElement(ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                        new XElement(ds + "DigestValue", "digestValue3")
                    )
                ),
                new XElement(ds + "SignatureValue"),
                new XElement(ds + "KeyInfo", new XAttribute("Id", keyInfoId),
                    new XElement(ds + "X509Data",
                        new XElement(ds + "X509IssuerSerial",
                            new XElement(ds + "X509IssuerName", x509IssuerName),
                            new XElement(ds + "X509SerialNumber", x509SerialNumber)
                        )
                    )
                ),
                new XElement(ds + "Object",
                    new XElement(xades + "QualifyingProperties", new XAttribute(XNamespace.Xmlns + "xades", xades), new XAttribute("Target", $"#{signatureId}"),
                        new XElement(xades + "SignedProperties", new XAttribute("Id", $"{signedPropsId}-signedprops"),
                            new XElement(xades + "SignedSignatureProperties",
                                new XElement(xades + "SigningTime", signingTime),
                                new XElement(xades + "SigningCertificate",
                                    new XElement(xades + "Cert",
                                        new XElement(xades + "CertDigest",
                                            new XElement(ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                                            new XElement(ds + "DigestValue", certificateDigest)),
                                        new XElement(xades + "IssuerSerial",
                                            new XElement(ds + "X509IssuerName", x509IssuerName),
                                            new XElement(ds + "X509SerialNumber", x509SerialNumber))))
                            )
                        )
                    )
                )
            )
        );

        return signatureDoc;
    }

    private static XDocument GenerateLegacy(string keyInfoId, string signedPropsId, string x509IssuerName, string x509SerialNumber, string signingTime, string signatureAlgorithm)
    {
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        XNamespace xades = "http://uri.etsi.org/01903/v1.3.2#";
        return new(
            new XElement(ds + "Signature", new XAttribute(XNamespace.Xmlns + "ds", ds),
                new XElement(ds + "SignedInfo",
                    new XElement(ds + "CanonicalizationMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#")),
                    new XElement(ds + "SignatureMethod", new XAttribute("Algorithm", signatureAlgorithm)),
                    Reference(ds, $"#{keyInfoId}"),
                    Reference(ds, $"#{signedPropsId}-signedprops", "http://uri.etsi.org/01903/v1.3.2#SignedProperties"),
                    Reference(ds, null)),
                new XElement(ds + "SignatureValue"),
                new XElement(ds + "KeyInfo", new XAttribute("Id", keyInfoId),
                    new XElement(ds + "X509Data",
                        new XElement(ds + "X509IssuerSerial",
                            new XElement(ds + "X509IssuerName", x509IssuerName),
                            new XElement(ds + "X509SerialNumber", x509SerialNumber)))),
                new XElement(ds + "Object",
                    new XElement(xades + "QualifyingProperties", new XAttribute(XNamespace.Xmlns + "xades", xades),
                        new XElement(xades + "SignedProperties", new XAttribute("Id", $"{signedPropsId}-signedprops"),
                            new XElement(xades + "SignedSignatureProperties",
                                new XElement(xades + "SigningTime", signingTime)))))));

        static XElement Reference(XNamespace ds, string? uri, string? type = null)
        {
            var element = new XElement(ds + "Reference");
            if (uri is not null) element.Add(new XAttribute("URI", uri));
            if (type is not null) element.Add(new XAttribute("Type", type));
            element.Add(
                new XElement(ds + "Transforms",
                    new XElement(ds + "Transform", new XAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#"))),
                new XElement(ds + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                new XElement(ds + "DigestValue", uri is null ? "digestValue3" : type is null ? "digestValue1" : "digestValue2"));
            return element;
        }
    }

    private static string SignatureAlgorithmURL(string algorithm)
    {
        return algorithm switch
        {
            "SHA1withRSA" => "http://www.w3.org/2000/09/xmldsig#rsa-sha1",
            "SHA256withRSA" => "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
            _ => throw new CryptographicException($"Unsupported signature algorithm: {algorithm}"),
        };
    }
}
