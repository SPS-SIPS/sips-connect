namespace SIPS.XMLDsig.Xades.Interfaces;

public interface ICertificateDownloadService
{
    Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string sn, string issuerDN, CancellationToken cancellationToken = default);
}