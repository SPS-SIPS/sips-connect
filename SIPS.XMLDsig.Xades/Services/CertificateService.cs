using System.Text;
using System.Xml;
using System.Xml.Linq;
using SIPS.XMLDsig.Xades.Options;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Security.Certificates;
using Org.BouncyCastle.X509;
namespace SIPS.XMLDsig.Xades.Services;
public sealed class CertificateService : ICertificateService
{
    public CertificateService(XadesOptions options)
    {
        _configuration = options;
        AsymmetricKey = LoadPrivateKey();
        Certificate = LoadCertificate();
        Chain = LoadCertificateChain();
    }
    private readonly XadesOptions _configuration;
    public AsymmetricKeyParameter AsymmetricKey { get; private set; }
    private readonly string currentDirectory = Directory.GetCurrentDirectory();

    public X509Certificate Certificate { get; private set; }
    public X509Certificate[] Chain { get; private set; }
    private AsymmetricKeyParameter LoadPrivateKey()
    {
        var prvKeyPath = _configuration.PrivateKeyPath ?? throw new ArgumentNullException("XadesConfig.PrivateKeyPath is required in appSettings.json");
        var prvKeyPassphrase = _configuration.PrivateKeyPassphrase;
        var privateKeyPem = File.ReadAllText(Path.Combine(currentDirectory, prvKeyPath));

        using StringReader reader = new(privateKeyPem);
        PemReader pemReader = !string.IsNullOrEmpty(prvKeyPassphrase)
            ? new PemReader(reader, new PasswordFinder(prvKeyPassphrase!))
            : new PemReader(reader);

        var obj = pemReader.ReadObject();

        if (obj is AsymmetricCipherKeyPair keyPair)
        {
            return keyPair.Private;
        }

        return (AsymmetricKeyParameter)obj;
    }

    private class PasswordFinder(string password) : IPasswordFinder
    {
        private readonly char[] _password = password.ToCharArray();

        public char[] GetPassword() => _password;
    }

    private X509Certificate[] LoadCertificateChain()
    {
        var chainPath = _configuration.ChainPath ?? throw new ArgumentNullException("XadesConfig.ChainPath is required in appSettings.json");
        var certificatePem = File.ReadAllText(Path.Combine(currentDirectory, chainPath));
        using StringReader reader = new(certificatePem);
        PemReader pemReader = new(reader);

        List<X509Certificate> certs = [];
        while (pemReader.ReadObject() is X509Certificate cert)
        {
            certs.Add(cert);
        }

        if (certs.Count == 0)
        {
            throw new Exception("No certificates found in the PEM file.");
        }

        try
        {
            foreach (var cert in certs)
            {
                // If self-signed, skip (root)
                if (cert.IssuerDN.Equivalent(cert.SubjectDN))
                {
                    cert.Verify(cert.GetPublicKey());
                    continue;
                }

                // Find issuer in the list
                var issuer = certs.FirstOrDefault(c => c.SubjectDN.Equivalent(cert.IssuerDN));
                if (issuer == null)
                    throw new Exception($"Issuer not found for certificate: {cert.SubjectDN}");

                cert.Verify(issuer.GetPublicKey());
            }
        }
        catch (Exception ex)
        {
            throw new Exception("The certificate chain is invalid.", ex);
        }

        return [.. certs];
    }

    public X509Certificate CertificateFromPem(string certPem)
    {
        using StringReader reader = new(certPem);
        PemReader pemReader = new(reader);

        List<X509Certificate> chain = [];
        while (pemReader.ReadObject() is X509Certificate cert)
        {
            chain.Add(cert);
        }

        if (chain.Count == 0)
        {
            throw new Exception("No certificates found in the PEM file.");
        }

        return chain[0];
    }

    private X509Certificate LoadCertificate()
    {
        var certPath = _configuration.CertificatePath ?? throw new ArgumentNullException("XadesConfig.CertificatePath is required in appSettings.json");
        var certificatePem = File.ReadAllText(Path.Combine(currentDirectory, certPath));
        using StringReader reader = new(certificatePem);
        PemReader pemReader = new(reader);
        return (X509Certificate)pemReader.ReadObject();
    }
    public XmlDocument GetSignatureElement(string keyInfoId, string signedPropsId, string signingTime, string algorithm)
    {
        keyInfoId = "_" + keyInfoId;
        signedPropsId = "_" + signedPropsId;
        var x509IssuerName = XmlSecurityHelpers.FormatIssuerDN(Certificate!);
        var x509SerialNumber = Certificate!.SerialNumber.ToString();

        XDocument signatureDoc = XmlSignatureGenerator.GenerateSignatureXml(keyInfoId, signedPropsId, x509IssuerName, x509SerialNumber, signingTime, algorithm);

        XmlDocument signatureElement = new()
        {
            PreserveWhitespace = false
        };
        signatureElement.LoadXml(signatureDoc.ToString());
        return signatureElement;
    }

    public string GetCertificatePem()
    {
        StringBuilder pemStringBuilder = new();

        using (TextWriter textWriter = new StringWriter(pemStringBuilder))
        {
            PemWriter pemWriter = new(textWriter);

            pemWriter.WriteObject(Certificate);

            pemWriter.Writer.Flush();
        }

        string pemString = pemStringBuilder.ToString();

        string base64Cert = pemString
            .Replace("-----BEGIN CERTIFICATE-----", string.Empty)
            .Replace("-----END CERTIFICATE-----", string.Empty)
            .Trim();

        string formattedCert = base64Cert.Replace("\r", string.Empty).Replace("\n", string.Empty);

        return formattedCert;
    }

    public (bool isValid, string? ex) CheckValidity(X509Certificate certificate)
    {
        try
        {
            foreach (var trustedCert in Chain)
            {
                certificate.Verify(trustedCert.GetPublicKey());
                certificate.CheckValidity();
                return (true, "");
            }
            return (false, "The certificate is not trusted.");
        }
        catch (InvalidKeyException ex) { return (false, ex.Message); }
        catch (SignatureException ex) { return (false, ex.Message); }
        catch (CertificateExpiredException ex) { return (false, ex.Message); }
        catch (CertificateNotYetValidException ex) { return (false, ex.Message); }
    }
}