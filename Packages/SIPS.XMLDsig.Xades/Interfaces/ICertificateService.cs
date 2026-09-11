using System.Xml;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;

namespace SIPS.XMLDsig.Xades.Interfaces;
public interface ICertificateService
{
    AsymmetricKeyParameter AsymmetricKey { get; }
    X509Certificate Certificate { get; }
    X509Certificate CertificateFromPem(string certPem);
    XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string businessLayerId, string signingTime, string algorithm);
    string GetCertificatePem();
    (bool isValid, string? ex) CheckValidity(X509Certificate certificate);
}
