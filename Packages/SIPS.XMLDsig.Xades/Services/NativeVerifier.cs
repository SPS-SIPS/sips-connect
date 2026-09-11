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

public class NativeVerifier(XadesOptions options, ILogger<NativeVerifier> logger, ICertificateService cs, ICertificateDownloadService certificateDownload, TimeProvider? timeProvider = null) : INativeVerifier
{
    private readonly ICertificateService _cs = cs;
    private readonly ILogger<NativeVerifier> _logger = logger;
    private readonly ICertificateDownloadService _cdService = certificateDownload;
    private readonly XadesOptions _configuration = options;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public async Task<SignatureVerificationResult> VerifyWithProvenance(string message, CancellationToken cancellationToken)
    {
        if (_configuration.WithoutPKI) return new(false,new VerboseResult{CertificateStatus="Not Verified",SignatureStatus="Not Verified",ReferencesStatus="Not Verified",OwnershSIPStatus="Not Verified"},null);
        try
        {
        var envelope = GetAsXmlDocument(message);
        var signature = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "ds:Signature");
        var keyInfo = GetReferencedKeyInfo(signature);
        var serial = keyInfo.GetElementsByTagName("X509SerialNumber", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single().InnerText;
        var issuer = keyInfo.GetElementsByTagName("X509IssuerName", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single().InnerText.Trim();
        var (record, error) = await _cdService.GetCertificatesAsync(serial, issuer, cancellationToken);
        if (record is null || record.Revoked || string.IsNullOrWhiteSpace(record.Authority) || string.IsNullOrWhiteSpace(record.Environment) || string.IsNullOrWhiteSpace(record.RepresentedParticipant) || string.IsNullOrWhiteSpace(record.CertificateSha256) || string.IsNullOrWhiteSpace(record.TrustProfileVersion) || string.IsNullOrWhiteSpace(record.RequiredExtendedKeyUsageOid))
        {
            var failed=new VerboseResult{CertificateStatus=error ?? "Signer provenance is incomplete or revoked",SignatureStatus="Not Verified",ReferencesStatus="Not Verified",OwnershSIPStatus="Not Verified"};
            return new(false, failed, null);
        }
        var basic = await VerifySignatureCore(message, true, cancellationToken, record);
        if (!basic.result) return new(false, basic.verbose, null);
        var certificate=_cs.CertificateFromPem(record.Content);var fingerprint=Convert.ToHexString(SHA256.HashData(certificate.GetEncoded())).ToLowerInvariant();
        if(!StringComparer.OrdinalIgnoreCase.Equals(fingerprint,record.CertificateSha256))return new(false,basic.verbose,null);
        var headerNs="urn:iso:std:iso:20022:tech:xsd:head.001.001.03";var app=envelope.GetElementsByTagName("AppHdr",headerNs).Cast<XmlElement>().Single();
        var definition=app.GetElementsByTagName("MsgDefIdr",headerNs).Cast<XmlElement>().First().InnerText;var service=app.GetElementsByTagName("BizSvc",headerNs).Cast<XmlElement>().FirstOrDefault()?.InnerText??string.Empty;
        var protectedHash=Convert.ToHexString(SHA256.HashData(CanonicalizeElement(envelope.DocumentElement!))).ToLowerInvariant();
        return new(true, basic.verbose, new(record.Owner, record.Authority, record.Environment, record.RepresentedParticipant, issuer, serial, fingerprint, true, record.TrustProfileVersion, definition, service, protectedHash, _timeProvider.GetUtcNow()));
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex,"SIPS signer provenance validation failed closed.");
            return new(false,new VerboseResult{CertificateStatus="Invalid",SignatureStatus="Invalid",ReferencesStatus=ex.Message,OwnershSIPStatus="Not Verified"},null);
        }
    }
    public Task<(bool result, VerboseResult verbose)> VerifySignature(string message, bool checkOwnerShip, CancellationToken cancellationToken)
        => VerifySignatureCore(message, checkOwnerShip, cancellationToken, null);
    private async Task<(bool result, VerboseResult verbose)> VerifySignatureCore(string message, bool checkOwnerShip, CancellationToken cancellationToken, SIPS.XMLDsig.Xades.Models.CertificateDownloadResponse? suppliedRecord)
    {
        _logger.LogDebug("Verifying the signature of the message.");
        if (_configuration.WithoutPKI)
        {
            return (false, new VerboseResult
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
            ValidateWpSipsProfile(envelope, signatureElement, signedInfoElement, signatureAlgorithm);
            var (isValid, signingCertificate, owner) = await ValidateCertificate(signatureElement, cancellationToken, suppliedRecord);

            if (!isValid)
            {
                _logger.LogError("Could not validate the certificate.");
                vr.CertificateStatus = "Invalid";
                return (false, vr);
            }
            vr.CertificateStatus = "Valid";
            ValidateSigningCertificateDigest(signatureElement, signingCertificate!);

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

            if (!VerifyValidationWindow(SigningTime.InnerText))
            {
                vr.SignatureStatus = "The signature is not within the validation window.";
                return (false, vr);
            }
            ValidateTimestampCorrelation(envelope, SigningTime.InnerText, _timeProvider.GetUtcNow());

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

    private static void ValidateWpSipsProfile(XmlDocument envelope, XmlElement signature, XmlElement signedInfo, XmlElement signatureMethod)
    {
        const string exc = "http://www.w3.org/2001/10/xml-exc-c14n#";
        const string sha256 = "http://www.w3.org/2001/04/xmlenc#sha256";
        const string rsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        if (envelope.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).Count != 1 ||
            signatureMethod.GetAttribute("Algorithm") != rsaSha256 ||
            GetFirstOfXmlElementsByTagWithPrefix(signedInfo, "ds:CanonicalizationMethod").GetAttribute("Algorithm") != exc)
            throw new CryptographicException("Signature algorithms or cardinality do not match the WP-SIPS profile.");
        var refs = signedInfo.GetElementsByTagName("Reference", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().ToArray();
        if (refs.Length != 3 || refs.Any(r => !r.GetAttribute("URI").StartsWith('#') || ((XmlElement?)r.GetElementsByTagName("DigestMethod", SignedXml.XmlDsigNamespaceUrl)[0])?.GetAttribute("Algorithm") != sha256))
            throw new CryptographicException("The WP-SIPS profile requires exactly three identified SHA-256 references.");
        var rootId = envelope.DocumentElement?.GetAttribute("Id");
        var ids = envelope.SelectNodes("//*[@Id]")!.Cast<XmlElement>().Select(x => x.GetAttribute("Id")).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new CryptographicException("Signature target Id values must be non-empty and unique.");
        foreach(var id in ids) try { XmlConvert.VerifyNCName(id); } catch(XmlException ex) { throw new CryptographicException("Signature target Id is not a valid XML ID.",ex); }
        var rootRef = refs.SingleOrDefault(r => r.GetAttribute("URI") == "#" + rootId) ?? throw new CryptographicException("BusinessLayer reference is missing.");
        var transforms = rootRef.GetElementsByTagName("Transform", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Select(x => x.GetAttribute("Algorithm")).ToArray();
        if (!transforms.SequenceEqual(new[] { SignedXml.XmlDsigEnvelopedSignatureTransformUrl, exc })) throw new CryptographicException("BusinessLayer transforms do not match the WP-SIPS profile.");
        var keyInfo = signature.GetElementsByTagName("KeyInfo", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single();
        var signedProperties = signature.GetElementsByTagName("SignedProperties", "http://uri.etsi.org/01903/v1.3.2#").Cast<XmlElement>().Single();
        var signatureId = signature.GetAttribute("Id");
        var qualifyingProperties = signature.GetElementsByTagName("QualifyingProperties", "http://uri.etsi.org/01903/v1.3.2#").Cast<XmlElement>().Single();
        if (string.IsNullOrWhiteSpace(signatureId) || qualifyingProperties.GetAttribute("Target") != "#" + signatureId)
            throw new CryptographicException("XAdES QualifyingProperties must target the identified Signature.");
        foreach (var target in new[] { keyInfo, signedProperties })
        {
            var reference = refs.SingleOrDefault(r => r.GetAttribute("URI") == "#" + target.GetAttribute("Id")) ?? throw new CryptographicException("KeyInfo/SignedProperties reference is missing.");
            var expectedType=ReferenceEquals(target,signedProperties)?"http://uri.etsi.org/01903/v1.3.2#SignedProperties":string.Empty;
            if(reference.GetAttribute("Type")!=expectedType)throw new CryptographicException("Reference role/type binding does not match the WP-SIPS profile.");
            var ts = reference.GetElementsByTagName("Transform", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Select(x => x.GetAttribute("Algorithm")).ToArray();
            if (!ts.SequenceEqual(new[] { exc })) throw new CryptographicException("KeyInfo/SignedProperties must use exclusive canonicalization only.");
        }
        if(rootRef.HasAttribute("Type"))throw new CryptographicException("BusinessLayer reference must not carry a reference Type.");
    }

    private static XmlElement GetReferencedKeyInfo(XmlElement signature)
    {
        var signedInfo=signature.GetElementsByTagName("SignedInfo",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single();
        var keyInfo=signature.GetElementsByTagName("KeyInfo",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single();
        var id=keyInfo.GetAttribute("Id");
        if(string.IsNullOrWhiteSpace(id)||signedInfo.GetElementsByTagName("Reference",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Count(x=>x.GetAttribute("URI")=="#"+id)!=1)throw new CryptographicException("A unique referenced KeyInfo is required.");
        if(keyInfo.GetElementsByTagName("X509IssuerName",SignedXml.XmlDsigNamespaceUrl).Count!=1||keyInfo.GetElementsByTagName("X509SerialNumber",SignedXml.XmlDsigNamespaceUrl).Count!=1)throw new CryptographicException("Referenced KeyInfo must contain one issuer/serial pair.");
        return keyInfo;
    }

    private static void ValidateSigningCertificateDigest(XmlElement signature, X509Certificate certificate)
    {
        var certDigest = signature.GetElementsByTagName("CertDigest", "http://uri.etsi.org/01903/v1.3.2#").Cast<XmlElement>().SingleOrDefault()?.GetElementsByTagName("DigestValue", SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().SingleOrDefault();
        if (certDigest is null || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(certDigest.InnerText), SHA256.HashData(certificate.GetEncoded())))
            throw new CryptographicException("SigningCertificate digest does not bind the downloaded certificate.");
        var issuerSerial=signature.GetElementsByTagName("IssuerSerial","http://uri.etsi.org/01903/v1.3.2#").Cast<XmlElement>().SingleOrDefault()??throw new CryptographicException("SigningCertificate IssuerSerial is missing.");
        var issuer=issuerSerial.GetElementsByTagName("X509IssuerName",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().SingleOrDefault()?.InnerText;
        var serial=issuerSerial.GetElementsByTagName("X509SerialNumber",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().SingleOrDefault()?.InnerText;
        if(!certificate.IssuerDN.ToString().Equals(issuer,StringComparison.Ordinal)||certificate.SerialNumber.ToString()!=serial)throw new CryptographicException("SigningCertificate IssuerSerial does not bind the downloaded certificate.");
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
                XmlElement? referencedElement = envelope.SelectSingleNode($"//*[@Id='{id}']", ns) as XmlElement
                    ?? throw new InvalidOperationException($"Referenced element with Id '{id}' not found.");
                var (isDigestValid, expected, computed) = VerifyDigest(reference, referencedElement);
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

    private async Task<(bool valid, X509Certificate? signingCertificate, string? owner)> ValidateCertificate(XmlElement signatureElement, CancellationToken cancellationToken = default, SIPS.XMLDsig.Xades.Models.CertificateDownloadResponse? suppliedRecord = null)
    {
        var keyInfo=GetReferencedKeyInfo(signatureElement);
        XmlElement sn = keyInfo.GetElementsByTagName("X509SerialNumber",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single();
        XmlElement issuer = keyInfo.GetElementsByTagName("X509IssuerName",SignedXml.XmlDsigNamespaceUrl).Cast<XmlElement>().Single();

        // BPC Hardening: Normalize IssuerDN to avoid cache misses due to formatting variance
        string normalizedIssuer = issuer.InnerText?.Trim() ?? string.Empty;
        while (normalizedIssuer.Contains("  ")) normalizedIssuer = normalizedIssuer.Replace("  ", " ");

        var certificate=suppliedRecord;string? error=null;
        if(certificate is null)(certificate,error)=await _cdService.GetCertificatesAsync(sn.InnerText, normalizedIssuer, cancellationToken);
        if (certificate is null)
        {
            _logger.LogError("Could not download the certificate: {error}", error);
            return (false, null, null);
        }
        if (certificate.Revoked) return (false, null, null);
        var SigningCert = _cs.CertificateFromPem(certificate.Content);
        var commonNames=SigningCert.SubjectDN.GetValueList(Org.BouncyCastle.Asn1.X509.X509Name.CN).Cast<object>().Select(x=>x.ToString()).ToArray();
        if(commonNames.Length!=1||!StringComparer.Ordinal.Equals(commonNames[0],certificate.Owner))return(false,null,null);
        var eku=SigningCert.GetExtendedKeyUsage()?.Cast<object>().Select(x=>x.ToString()).ToArray();
        if(eku is null||!eku.Contains(certificate.RequiredExtendedKeyUsageOid,StringComparer.Ordinal))return(false,null,null);
        var fingerprint=Convert.ToHexString(SHA256.HashData(SigningCert.GetEncoded())).ToLowerInvariant();if(!StringComparer.OrdinalIgnoreCase.Equals(fingerprint,certificate.CertificateSha256))return(false,null,null);
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

        if (element.LocalName != "Signature")
        {
            var signatures = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
                .Cast<XmlNode>().ToArray();
            foreach (var signature in signatures)
                signature.ParentNode?.RemoveChild(signature);
        }

        XmlDsigExcC14NTransform transform = new();
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

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var validTo = signingTime.AddMinutes(Math.Min(_configuration.VerificationWindowMinutes, 100));
        var isValid = signingTime <= now.AddMinutes(5) && now <= validTo;
        return isValid;
    }

    private static void ValidateTimestampCorrelation(XmlDocument envelope, string signingTimeText, DateTimeOffset now)
    {
        var creation = GetFirstOfXmlElementsByTagWithPrefix(envelope.DocumentElement!, "header:CreDt").InnerText;
        static bool HasExplicitZone(string value) => value.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!HasExplicitZone(creation) || !HasExplicitZone(signingTimeText)) throw new CryptographicException("CreDt and SigningTime require an explicit timezone.");
        if (!DateTimeOffset.TryParse(creation, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var created) ||
            !DateTimeOffset.TryParse(signingTimeText, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var signed) ||
            created.Offset != TimeSpan.Zero || signed.Offset != TimeSpan.Zero) throw new CryptographicException("CreDt and SigningTime must be timezone-qualified UTC timestamps.");
        if (created > now.AddMinutes(5) || created < now.AddMinutes(-100) || signed > created.AddMinutes(5)) throw new CryptographicException("BAH/signature timestamp correlation is outside the WP-SIPS window.");
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
