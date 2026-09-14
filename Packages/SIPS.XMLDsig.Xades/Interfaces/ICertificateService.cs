using System.Xml;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;

namespace SIPS.XMLDsig.Xades.Interfaces;
public interface ICertificateService
{
    AsymmetricKeyParameter AsymmetricKey { get; }
    X509Certificate Certificate { get; }
    X509Certificate CertificateFromPem(string certPem);
    XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string signingTime, string algorithm)
        => throw new NotSupportedException("This certificate service does not implement the IPS vendor legacy XAdES profile.");
    XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string businessLayerId, string signingTime, string algorithm)
        => string.IsNullOrEmpty(businessLayerId)
            ? GetSignatureElement(keyInfoId, signedPropsId, signingTime, algorithm)
            : throw new NotSupportedException("This certificate service does not implement an identified business-layer XAdES reference.");
    XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string businessLayerId, string signingTime, string algorithm, XadesProfile profile)
        => profile == XadesProfile.IpsVendorLegacy
            ? GetSignatureElement(keyInfoId, signedPropsId, signingTime, algorithm)
            : GetSignatureElement(keyInfoId, signedPropsId, businessLayerId, signingTime, algorithm);
    string GetCertificatePem();
    (bool isValid, string? ex) CheckValidity(X509Certificate certificate);
    (bool isValid, string? ex) CheckValidity(X509Certificate certificate, XadesProfile profile)
        => CheckValidity(certificate);
}
