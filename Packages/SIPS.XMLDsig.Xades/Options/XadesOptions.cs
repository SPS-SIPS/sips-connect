namespace SIPS.XMLDsig.Xades.Options;

public class XadesOptions
{
    public string? CertificatePath { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string? ChainPath { get; set; }
    public string[]? Algorithms { get; set; }
    public int VerificationWindowMinutes { get; set; } = 100;
    public string BIC { get; set; } = string.Empty;
    /// <summary>SHA-256 DER fingerprints of authorized domestic switch signing certificates.
    /// Provision through a trusted channel. Multiple entries support overlapping rotation.</summary>
    public string[] DomesticSwitchCertificateSha256 { get; set; } = [];
    public bool WithoutPKI { get; set; } = false;
    public string DefaultSignatureMethod { get; set; } = "SHA256withRSA";
    public string BaseDN { get; set; } = string.Empty;
}
