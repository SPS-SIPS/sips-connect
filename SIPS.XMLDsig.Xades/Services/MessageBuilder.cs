using System.Xml.Linq;

namespace SIPS.XMLDsig.Xades.Services;
public static class AdminMessage
{
    public static string Generate(string reason, string? additionalData = null)
    {
        XNamespace admiNs = "urn:iso:std:iso:20022:tech:xsd:admi.002.001.01";

        XElement document = new(admiNs + "Document",
            new XElement(admiNs + "admi.002.001.01",
                new XElement(admiNs + "RltdRef",
                    new XElement(admiNs + "Ref", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                ),
                new XElement(admiNs + "Rsn",
                    new XElement(admiNs + "RjctgPtyRsn", reason),
                    AdditionalData(admiNs, null, additionalData)
                )
            )
        );

        return document.ToString();
    }
    public static string Generate(VerboseResult? result, string reason, string? additionalData)
    {
        XNamespace admiNs = "urn:iso:std:iso:20022:tech:xsd:admi.002.001.01";

        XElement document = new(admiNs + "Document",
            new XElement(admiNs + "admi.002.001.01",
                new XElement(admiNs + "RltdRef",
                    new XElement(admiNs + "Ref", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                ),
                new XElement(admiNs + "Rsn",
                    new XElement(admiNs + "RjctgPtyRsn", reason),
                    AdditionalData(admiNs, result, additionalData)
                )
            )
        );

        return document.ToString();
    }

    private static XElement? AdditionalData(XNamespace admiNs, VerboseResult? result, string? additionalData)
    {
        if (result is null)
        {
            if (additionalData is not null)
            {
                return new XElement(admiNs + "AddtlData", additionalData);
            }
            return null;
        }
        return new XElement(admiNs + "AddtlData",
            $"Cert Status: {result?.CertificateStatus}, " +
            $"Ref Status: {result?.ReferencesStatus}, " +
            $"Sig Status: {result?.SignatureStatus}, " +
            $"Ownership Status: {result?.OwnershSIPStatus}"
        );
    }
}