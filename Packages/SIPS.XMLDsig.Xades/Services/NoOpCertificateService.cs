using System.Xml;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using SIPS.XMLDsig.Xades.Interfaces;

namespace SIPS.XMLDsig.Xades.Services;

/// <summary>
/// A No-Op implementation of <see cref="ICertificateService"/> that throws exceptions for all members.
/// This prevents the system from attempting to load certificate files from disk when PKI mode is disabled.
/// </summary>
public sealed class NoOpCertificateService : ICertificateService
{
    private const string ErrorMessage = "Skipped in PKI-off mode. This member should not be accessed when WithoutPKI is enabled.";

    public NoOpCertificateService(ILogger<NoOpCertificateService> logger)
    {
        logger.LogWarning("🛡️ NoOpCertificateService initialized. Certificate file loading is SKIPPED.");
    }

    public AsymmetricKeyParameter AsymmetricKey => throw new InvalidOperationException(ErrorMessage);

    public X509Certificate Certificate => throw new InvalidOperationException(ErrorMessage);

    public X509Certificate CertificateFromPem(string certPem) => throw new InvalidOperationException(ErrorMessage);

    public (bool isValid, string? ex) CheckValidity(X509Certificate certificate) => throw new InvalidOperationException(ErrorMessage);

    public (bool isValid, string? ex) CheckValidity(X509Certificate certificate, XadesProfile profile) => throw new InvalidOperationException(ErrorMessage);

    public string GetCertificatePem() => throw new InvalidOperationException(ErrorMessage);

    public XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string signingTime, string algorithm)
        => throw new InvalidOperationException(ErrorMessage);

    public XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string businessLayerId, string signingTime, string algorithm)
        => throw new InvalidOperationException(ErrorMessage);

    public XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string businessLayerId, string signingTime, string algorithm, XadesProfile profile)
        => throw new InvalidOperationException(ErrorMessage);
}
