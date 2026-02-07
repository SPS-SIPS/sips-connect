using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Xml;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using System.Text;
using Org.BouncyCastle.X509;
using static SIPS.XMLDsig.Xades.Helpers.XmlSecurityHelpers;
using SIPS.XMLDsig.Xades.Options;

namespace SIPS.XMLDsig.Xades.Services;

public class NativeVerifier(XadesOptions options, ILogger<NativeVerifier> logger, ICertificateService cs, ICertificateDownloadService certificateDownload) : INativeVerifier
{
    private readonly ICertificateService _cs = cs;
    private readonly ILogger<NativeVerifier> _logger = logger;
    private readonly ICertificateDownloadService _cdService = certificateDownload;
    private readonly XadesOptions _configuration = options;
    public async Task<(bool result, VerboseResult verbose)> VerifySignature(string message, bool checkOwnerShip, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Verifying the signature of the message.");
        if (_configuration.WithoutPKI)
        {
            return (true, new VerboseResult
            {
                CertificateStatus = "Not Verified",
                SignatureStatus = "Not Verified",
                ReferencesStatus = "Not Verified",
                OwnershSIPStatus = "Not Verified"
            });
        }
        var vr = new VerboseResult
        {
            CertificateStatus = "Not Verified",
            SignatureStatus = "Not Verified",
            ReferencesStatus = "Not Verified",
            OwnershSIPStatus = "Not Verified"
        };
        try
        {
            XmlDocument envelope = GetAsXmlDocument(message);

            XmlNamespaceManager ns = new(envelope.NameTable);
            ns.AddNamespace("document", GetDocumentNamespace(envelope));
            ns.AddNamespace("ds", GetDocumentNamespace(envelope, "ds"));
            ns.AddNamespace(XadesPrefix, GetDocumentNamespace(envelope, XadesPrefix));
            XmlElement? signatureElement = GetFirstOfXmlElementsByTagOrNull(envelope.DocumentElement!, "ds:Signature");
            if (signatureElement == null)
            {
                _logger.LogWarning("The message does not contain a signature.");
                vr.SignatureStatus = "Signature element missing";
                return (false, vr);
            }
            XmlElement signedInfoElement = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "ds:SignedInfo");
            XmlElement signatureAlgorithm = GetFirstOfXmlElementsByTagWithPrefix(signedInfoElement, "ds:SignatureMethod");
            XmlElement SigningTime = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "xades:SigningTime");
            var (isValid, signingCertificate, owner) = await ValidateCertificate(signatureElement, cancellationToken);

            if (!isValid)
            {
                _logger.LogError("Could not validate the certificate.");
                vr.CertificateStatus = "Invalid";
                return (false, vr);
            }
            vr.CertificateStatus = "Valid";

            if (checkOwnerShip)
            {
                var checkOn = CheckTransactionOwnerAgainstCertificate(envelope, owner ?? "x");
                if (!checkOn)
                {
                    vr.OwnershSIPStatus = "Invalid";
                    _logger.LogError("The owner of the transaction does not match the owner of the certificate.");
                    return (false, vr);
                }
                vr.OwnershSIPStatus = "Valid";
            }

            if (!VerifyReferences(envelope, ns, signatureElement, signedInfoElement, vr))
            {
                return (false, vr);
            }

            // if (!VerifyValidationWindow(SigningTime.InnerText))
            // {
            //     vr.SignatureStatus = "The signature is not within the validation window.";
            //     _logger.LogError("The signature is not within the validation window.");
            //     return (false, vr);
            // }

            var isSignatureValid = VerifySignatureValue(signatureElement, signedInfoElement, signingCertificate!, signatureAlgorithm);
            vr.SignatureStatus = isSignatureValid ? "Valid" : "Invalid";
            return (
                isSignatureValid,
                vr
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while verifying the signature.");
            vr.SignatureStatus = ex.Message;
            return (false, vr);
        }
    }

    private bool VerifyReferences(XmlDocument envelope, XmlNamespaceManager ns, XmlElement signatureElement, XmlElement signedInfoElement, VerboseResult vr)
    {
        // Verify the digest values for each reference
        XmlNodeList references = signedInfoElement.GetElementsByTagName("Reference", SignedXml.XmlDsigNamespaceUrl);
        foreach (XmlElement reference in references)
        {
            string uri = reference.GetAttribute("URI");
            if (uri.StartsWith('#'))
            {
                string id = uri[1..];
                XmlElement? referencedElement = signatureElement.SelectSingleNode($"//*[@Id='{id}']", ns) as XmlElement
                    ?? throw new InvalidOperationException($"Referenced element with Id '{id}' not found.");
                var (isDigestValid, expected, computed) = VerifyDigest(reference, referencedElement);
                if (!isDigestValid)
                {
                    vr.ReferencesStatus = $"The digest value for the reference with URI '{uri}' is not valid, expected: {expected}, computed: {computed}";
                    _logger.LogError("The digest value for the reference with URI '{uri}' is not valid, expected: {expected}, computed: {computed}", uri, expected, computed);
                    return false;
                }
            }
            else if (string.IsNullOrEmpty(uri))
            {
                var anonymousDataObjectNode = (XmlElement?)envelope.SelectSingleNode("//document:Document", ns) ?? throw new InvalidOperationException("document:Document not found.");
                var (isDigestValid, expected, computed) = VerifyDigest(reference, anonymousDataObjectNode);
                if (!isDigestValid)
                {
                    vr.ReferencesStatus = $"The digest value for the reference with URI '{uri}' is not valid, expected: {expected}, computed: {computed}";
                    _logger.LogError("The digest value for the reference with URI '{uri}' is not valid, expected: {expected}, computed: {computed}", uri, expected, computed);
                    return false;
                }
            }
            else
            {
                vr.ReferencesStatus = $"The reference URI '{uri}' is not supported.";
                _logger.LogError("The reference URI '{uri}' is not supported.", uri);
                return false;
            }
        }
        vr.ReferencesStatus = "Valid";
        return true;
    }

    private async Task<(bool valid, X509Certificate? signingCertificate, string? owner)> ValidateCertificate(XmlElement signatureElement, CancellationToken cancellationToken = default)
    {
        XmlElement sn = GetFirstOfXmlElementsByTagWithPrefix(signatureElement!, "ds:X509SerialNumber") ?? throw new InvalidOperationException("XML must contain ds:X509SerialNumber node");
        XmlElement issuer = GetFirstOfXmlElementsByTagWithPrefix(signatureElement!, "ds:X509IssuerName") ?? throw new InvalidOperationException("XML must contain ds:X509IssuerName node");

        // BPC Hardening: Normalize IssuerDN to avoid cache misses due to formatting variance
        string normalizedIssuer = issuer.InnerText?.Trim() ?? string.Empty;
        while (normalizedIssuer.Contains("  ")) normalizedIssuer = normalizedIssuer.Replace("  ", " ");

        var (certificate, error) = await _cdService.GetCertificatesAsync(sn.InnerText, normalizedIssuer, cancellationToken);
        if (certificate is null)
        {
            _logger.LogError("Could not download the certificate: {error}", error);
            return (false, null, null);
        }
        var SigningCert = _cs.CertificateFromPem(certificate.Content);
        var (isValid, ex) = _cs.CheckValidity(SigningCert);
        if (!isValid)
        {
            _logger.LogError("The certificate in the signature is not valid: {ex}", ex);
            return (false, null, null);
        }

        return (true, SigningCert, certificate.Owner);
    }

    private static byte[] CanonicalizeElement(XmlElement element)
    {
        XmlDocument doc = new();
        doc.AppendChild(doc.ImportNode(element, true));

        XmlDsigC14NTransform transform = new();
        transform.LoadInput(doc);
        using Stream stream = (Stream)transform.GetOutput(typeof(Stream));
        using StreamReader reader = new(stream);
        var data = reader.ReadToEnd();
        return Encoding.UTF8.GetBytes(data);
    }

    private static bool VerifySignatureValue(XmlElement signatureElement, XmlElement signedInfoElement, X509Certificate certificate, XmlElement signatureAlgorithm)
    {
        XmlElement signatureValueElement = GetFirstOfXmlElementsByTagWithPrefix(signatureElement, "ds:SignatureValue") ?? throw new InvalidOperationException("XML must contain ds:SignatureValue node");

        byte[] signatureValue = Convert.FromBase64String(signatureValueElement.InnerText);
        byte[] canonicalizedSignedInfo = CanonicalizeElement(signedInfoElement);

        var algorithm = GetAlgorithmName(signatureAlgorithm?.Attributes["Algorithm"]?.Value ?? "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");

        ISigner verifier = SignerUtilities.GetSigner(algorithm);
        verifier.Init(false, certificate.GetPublicKey());
        verifier.BlockUpdate(canonicalizedSignedInfo, 0, canonicalizedSignedInfo.Length);

        bool isVerified = verifier.VerifySignature(signatureValue);

        return isVerified;
    }

    private static string GetAlgorithmName(string algorithm)
    {
        return algorithm switch
        {
            "http://www.w3.org/2000/09/xmldsig#rsa-sha1" => "SHA1withRSA",
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256" => "SHA256withRSA",
            _ => throw new CryptographicException($"Unsupported signature algorithm: {algorithm}"),
        };
    }

    private static (bool result, string expected, string computed) VerifyDigest(XmlElement reference, XmlElement referencedElement)
    {
        XmlElement digestValueElement = GetFirstOfXmlElementsByTagWithPrefix(reference, "ds:DigestValue") ?? throw new InvalidOperationException("XML must contain ds:SignatureValue node");

        byte[] digestValue = Convert.FromBase64String(digestValueElement.InnerText);

        byte[] canonicalizedData = CanonicalizeElement(referencedElement);

        var digestMethodNode = reference.GetElementsByTagName("DigestMethod", SignedXml.XmlDsigNamespaceUrl)[0];
        if (digestMethodNode?.Attributes?["Algorithm"] == null)
        {
            throw new InvalidOperationException("DigestMethod or its Algorithm attribute is missing.");
        }
        string digestMethod = digestMethodNode.Attributes["Algorithm"]?.Value
            ?? throw new InvalidOperationException("Algorithm attribute is missing in DigestMethod.");
        byte[] computedDigest = ComputeDigest(canonicalizedData, digestMethod);

        return (AreDigestsEqual(digestValue, computedDigest), Convert.ToBase64String(digestValue), Convert.ToBase64String(computedDigest));
    }

    private static byte[] ComputeDigest(byte[] data, string algorithmUri)
    {
        HashAlgorithm hashAlgorithm = algorithmUri switch
        {
            "http://www.w3.org/2000/09/xmldsig#sha1" => SHA1.Create(),
            "http://www.w3.org/2001/04/xmlenc#sha256" => SHA256.Create(),
            "http://www.w3.org/2001/04/xmlenc#sha384" => SHA384.Create(),
            "http://www.w3.org/2001/04/xmlenc#sha512" => SHA512.Create(),
            _ => throw new CryptographicException($"Unsupported digest algorithm: {algorithmUri}"),
        };
        return hashAlgorithm.ComputeHash(data);
    }

    private bool VerifyValidationWindow(string date)
    {
        // Parse the signing time as UTC
        if (!DateTime.TryParse(date, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var signingTime))
        {
            throw new ArgumentException("Invalid date format. Ensure the date is in a valid ISO 8601 format.");
        }

        var validTo = signingTime.AddMinutes(_configuration.VerificationWindowMinutes);
        var isValid = DateTime.UtcNow <= validTo;
        return isValid;
    }


    private static bool AreDigestsEqual(byte[] digest1, byte[] digest2)
    {
        if (digest1.Length != digest2.Length)
        {
            return false;
        }

        for (int i = 0; i < digest1.Length; i++)
        {
            if (digest1[i] != digest2[i])
            {
                return false;
            }
        }

        return true;
    }
}
