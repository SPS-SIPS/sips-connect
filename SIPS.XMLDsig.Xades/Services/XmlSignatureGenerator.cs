using System.Security.Cryptography;
using System.Xml.Linq;
namespace SIPS.XMLDsig.Xades.Services;
public static class XmlSignatureGenerator
{
    public static XDocument GenerateSignatureXml(string keyInfoId, string signedPropsId, string x509IssuerName, string x509SerialNumber, string signingTime, string algorithm)
    {
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        XNamespace xades = "http://uri.etsi.org/01903/v1.3.2#";
        var signatureAlgorithm = SignatureAlgorithmURL(algorithm);
        XDocument signatureDoc = new(
            new XElement(ds + "Signature", new XAttribute(XNamespace.Xmlns + "ds", ds),
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
                    new XElement(ds + "Reference",
                        new XElement(ds + "Transforms",
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
                    new XElement(xades + "QualifyingProperties", new XAttribute(XNamespace.Xmlns + "xades", xades),
                        new XElement(xades + "SignedProperties", new XAttribute("Id", $"{signedPropsId}-signedprops"),
                            new XElement(xades + "SignedSignatureProperties",
                                new XElement(xades + "SigningTime", signingTime)
                            )
                        )
                    )
                )
            )
        );

        return signatureDoc;
    }

    private static string SignatureAlgorithmURL(string algorithm)
    {
        return algorithm switch
        {
            "SHA1withRSA" => "http://www.w3.org/2000/09/xmldsig#rsa-sha1",
            "SHA256withRSA" => "http://www.w3.org/2001/04/xmldsig#rsa-sha256",
            // "SHA384withRSA" => "http://www.w3.org/2000/09/xmldsig#rsa-sha384",
            // "SHA512withRSA" => "http://www.w3.org/2000/09/xmldsig#rsa-sha512",
            _ => throw new CryptographicException($"Unsupported signature algorithm: {algorithm}"),
        };
    }
}
