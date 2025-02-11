namespace SIPS.XMLDsig.Xades.Interfaces;

public interface ICertificateDownloadService
{
    Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string sn, CancellationToken cancellationToken = default);
}