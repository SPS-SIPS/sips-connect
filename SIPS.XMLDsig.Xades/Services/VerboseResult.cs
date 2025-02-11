namespace dsig.Services;

public sealed class VerboseResult
{
    public string CertificateStatus { get; set; } = "";
    public string ReferencesStatus { get; set; } = "";
    public string SignatureStatus { get; set; } = "";
    public string OwnershSIPStatus { get; set; } = "";
}