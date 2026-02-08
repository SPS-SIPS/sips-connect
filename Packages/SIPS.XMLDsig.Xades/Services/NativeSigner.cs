using System.Security.Cryptography;
using System.Xml;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using System.Text;
using static SIPS.XMLDsig.Xades.Helpers.XmlSecurityHelpers;
using SIPS.XMLDsig.Xades.Options;
using System.Security.Cryptography.Xml;
namespace SIPS.XMLDsig.Xades.Services;
public class NativeSigner(XadesOptions options, ILogger<NativeSigner> logger, ICertificateService cs) : INativeSigner
{
    private readonly ILogger<NativeSigner> _logger = logger;
    private readonly ICertificateService _cs = cs;
    private readonly XadesOptions _configuration = options;
    public string SignEnvelope(string message, string algorithmX = "SHA256withRSA")
    {
        if (_configuration.WithoutPKI)
        {
            return message;
        }
        if (!VerifyIfAlgorithmIsSupported(_configuration.DefaultSignatureMethod))
        {
            _logger.LogError("The provided algorithm is not supported.");
            throw new InvalidOperationException("The provided algorithm is not supported.");
        }
        XmlDocument signatureTemplate = _cs.GetSignatureElement(
             Guid.NewGuid().ToString(),
             Guid.NewGuid().ToString(),
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            algorithm: _configuration.DefaultSignatureMethod
            );

        XmlElement? signatureElement = signatureTemplate.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0] as XmlElement
            ?? throw new InvalidOperationException("Signature element not found.");

        XmlDocument envelope = GetAsXmlDocument(message);

        // if (!CheckIfTheMessageAllowed(envelope, BIC: _configuration.BIC, isSigningType: true))
        // {
        //     throw new InvalidOperationException("You are not allowed to sign this message.");
        // }

        XmlNamespaceManager ns = new(envelope.NameTable);
        ns.AddNamespace("document", GetDocumentNamespace(envelope));
        ns.AddNamespace("header", GetDocumentNamespace(envelope, "header"));
        ns.AddNamespace("ds", GetDocumentNamespace(envelope, "ds"));
        ns.AddNamespace("xades", GetDocumentNamespace(envelope, XadesPrefix));

        var appHeader = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "header:AppHdr");
        // create the document signature element
        XmlElement documentSgntr = envelope.CreateElement("document", "Sgntr", "urn:iso:std:iso:20022:tech:xsd:head.001.001.03");
        // Add the signature to the AppHdr
        appHeader.AppendChild(documentSgntr);
        // AttachCertificatePem(signatureElement!, ns);
        // Update References
        UpdateReferences(signatureElement!, envelope);

        // Compute the new signature
        RecomputeSignatureWithBouncyCastle(signatureElement!, _configuration.DefaultSignatureMethod);

        // Append the signature to the XML document
        documentSgntr.AppendChild(envelope.ImportNode(signatureElement!, true));
        return envelope.OuterXml;
    }

    private static void UpdateReferences(XmlElement signatureElement, XmlDocument envelope)
    {
        XmlNamespaceManager ns = new(envelope.NameTable);
        ns.AddNamespace("document", GetDocumentNamespace(envelope));
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        ns.AddNamespace("xades", "http://uri.etsi.org/01903/v1.3.2#");

        XmlNodeList references = signatureElement.GetElementsByTagName("Reference", SignedXml.XmlDsigNamespaceUrl);
        foreach (XmlElement reference in references)
        {
            string uri = reference.GetAttribute("URI");
            if (uri.StartsWith("#"))
            {
                string id = uri[1..];
                XmlElement elementToDigest = (XmlElement?)signatureElement.SelectSingleNode($"//*[@Id='{id}']", ns)
                    ?? throw new InvalidOperationException($"Element with Id '{id}' not found.");
                // Canonicalize the element and compute the digest
                byte[] digest = ComputeDigest(elementToDigest);

                // Update the DigestValue
                XmlElement? digestValueElement = reference.GetElementsByTagName("DigestValue", SignedXml.XmlDsigNamespaceUrl)[0] as XmlElement ?? throw new InvalidOperationException("DigestValue element not found.");
                digestValueElement.InnerText = Convert.ToBase64String(digest);
            }
            else if (uri == "")
            {
                // Canonicalize the entire document and compute the digest
                var anonymousDataObjectNode = (XmlElement?)envelope.SelectSingleNode("//document:Document", ns) ?? throw new InvalidOperationException("document:Document not found.");
                byte[] digest = ComputeDigest(anonymousDataObjectNode);

                // Update the DigestValue
                XmlElement? digestValueElement = reference.GetElementsByTagName("DigestValue", SignedXml.XmlDsigNamespaceUrl)[0] as XmlElement
                    ?? throw new InvalidOperationException("DigestValue element not found.");
                digestValueElement.InnerText = Convert.ToBase64String(digest);
            }
            else
            {
                throw new InvalidOperationException("Invalid URI in Reference element.");
            }
        }
    }


    private void RecomputeSignatureWithBouncyCastle(XmlElement signatureElement, string algorithm)
    {
        // Get the SignedInfo element
        XmlElement? signedInfoElement = signatureElement.GetElementsByTagName("SignedInfo", SignedXml.XmlDsigNamespaceUrl)[0] as XmlElement
            ?? throw new InvalidOperationException("SignedInfo element not found.");
        string canonicalizedSignedInfo = CanonicalizeElement(signedInfoElement);

        // Use Bouncy Castle to sign the hash
        byte[] signatureValue = SignWithBouncyCastle(canonicalizedSignedInfo, algorithm: algorithm);

        // Update the signature value in the Signature element
        XmlElement? signatureValueElement = signatureElement.GetElementsByTagName("SignatureValue", SignedXml.XmlDsigNamespaceUrl)[0] as XmlElement
            ?? throw new InvalidOperationException("SignatureValue element not found.");
        signatureValueElement.InnerText = Convert.ToBase64String(signatureValue);
    }

    private static string CanonicalizeElement(XmlElement element)
    {
        XmlDocument doc = new();
        doc.AppendChild(doc.ImportNode(element, true));

        XmlDsigC14NTransform transform = new();
        transform.LoadInput(doc);
        using Stream stream = (Stream)transform.GetOutput(typeof(Stream));
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private byte[] SignWithBouncyCastle(string data, string algorithm)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(data);

        ISigner signer = SignerUtilities.GetSigner(algorithm);
        signer.Init(true, _cs.AsymmetricKey);
        signer.BlockUpdate(dataBytes, 0, dataBytes.Length);

        byte[] signature = signer.GenerateSignature();
        return signature;
    }

    private static byte[] ComputeDigest(XmlElement element)
    {
        XmlDocument doc = new();
        doc.AppendChild(doc.ImportNode(element, true));
        // Canonicalize the element
        XmlDsigC14NTransform transform = new();
        transform.LoadInput(doc);

        using MemoryStream ms = new();
        using Stream s = (Stream)transform.GetOutput(typeof(Stream));
        s.CopyTo(ms);
        byte[] canonicalizedData = ms.ToArray();

        // Compute the digest using SHA-256
        return SHA256.HashData(canonicalizedData);
    }

    private bool VerifyIfAlgorithmIsSupported(string algorithm)
    {
        var supportedAlgorithms = _configuration.Algorithms ?? throw new InvalidOperationException("XadesConfig:Algorithms not found in appSettings.json.");
        return supportedAlgorithms.Contains(algorithm);
    }
}
