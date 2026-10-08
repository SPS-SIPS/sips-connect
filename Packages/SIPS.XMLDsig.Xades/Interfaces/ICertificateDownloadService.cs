namespace SIPS.XMLDsig.Xades.Interfaces;

public interface ICertificateDownloadService
{
    /// <param name="applyPapssTrustBinding">
    /// True only for the WP-SIPS/PAPSS profile. A certificate lookup for any other profile (e.g. the domestic
    /// IPS/SmartVista legacy profile used by SVIP) must never be subject to PAPSS's responder-owner restriction -
    /// that binding has no meaning for a domestic signer. Defaults to false so a caller that does not pass this
    /// explicitly gets the safe, unrestricted lookup rather than silently inheriting PAPSS-only trust rules.
    /// </param>
    Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string sn, string issuerDN, CancellationToken cancellationToken = default, bool applyPapssTrustBinding = false);
}