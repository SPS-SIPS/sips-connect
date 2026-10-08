using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SIPS.Connect.Config;
using SIPS.Core.Services;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Connect.Services;

/// <summary>
/// Adds the explicitly configured PAPSS trust-domain binding to a legacy
/// Guevara response. The legacy endpoint supplies the certificate, owner and
/// revocation state but does not yet publish the WP-SIPS provenance fields.
/// No binding is inferred unless the downloaded public certificate matches
/// the configured SHA-256 pin and PAPSS owner exactly (case-insensitive).
/// </summary>
public sealed class PapssResponderCertificateDownloadService(
    CertificateDownloadService inner,
    PapssFacingOptions options,
    ILogger<PapssResponderCertificateDownloadService> logger) : ICertificateDownloadService
{
    public async Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(
        string serialNumber,
        string issuerDN,
        CancellationToken cancellationToken = default,
        bool applyPapssTrustBinding = false)
    {
        var (record, error) = await inner.GetCertificatesAsync(serialNumber, issuerDN, cancellationToken);
        // Only a WP-SIPS/PAPSS lookup (applyPapssTrustBinding=true, set by the caller from the signature's own
        // XAdES profile) may ever be rejected or enriched for PAPSS's owner/trust-pin binding. A domestic IPS
        // (SmartVista/SVIP) certificate lookup has no PAPSS provenance to begin with - that is expected, not an
        // error - and must be returned as-is. Previously this ran unconditionally for every certificate lookup
        // in the process (there is only one ICertificateDownloadService registration), so any SVIP-signed message
        // was rejected with "The PAPSS responder certificate owner does not match the configured WP-SIPS identity"
        // before its own (profile-correct) certificate validation in NativeVerifier ever ran.
        if (!applyPapssTrustBinding || record is null || !options.Enabled || HasCompleteProvenance(record)) return (record, error);

        if (record.Revoked ||
            !StringComparer.OrdinalIgnoreCase.Equals(record.Owner?.Trim(), options.RemoteWpSipsIdentity.Trim()))
            return (null, "The PAPSS responder certificate owner does not match the configured WP-SIPS identity.");

        string fingerprint;
        try
        {
            using var certificate = X509Certificate2.CreateFromPem(record.Content);
            fingerprint = Convert.ToHexString(SHA256.HashData(certificate.Export(X509ContentType.Cert))).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            logger.LogWarning(exception, "The downloaded PAPSS responder certificate could not be parsed.");
            return (null, "The PAPSS responder certificate could not be parsed.");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(fingerprint, options.ResponderTrust.CertificateSha256))
        {
            logger.LogWarning("The downloaded PAPSS responder certificate does not match the configured SHA-256 pin.");
            return (null, "The PAPSS responder certificate does not match the configured SHA-256 pin.");
        }

        logger.LogInformation(
            "Applied configured PAPSS responder trust binding for {Participant} in {Environment}.",
            options.RemoteWpSipsIdentity,
            options.Environment);
        return (record with
        {
            Authority = options.ResponderTrust.Authority,
            Environment = options.Environment,
            RepresentedParticipant = options.RemoteWpSipsIdentity,
            CertificateSha256 = fingerprint,
            TrustProfileVersion = options.ResponderTrust.TrustProfileVersion,
            RequiredExtendedKeyUsageOid = options.ResponderTrust.RequiredExtendedKeyUsageOid
        }, null);
    }

    private static bool HasCompleteProvenance(CertificateDownloadResponse record) =>
        !string.IsNullOrWhiteSpace(record.Authority) &&
        !string.IsNullOrWhiteSpace(record.Environment) &&
        !string.IsNullOrWhiteSpace(record.RepresentedParticipant) &&
        !string.IsNullOrWhiteSpace(record.CertificateSha256) &&
        !string.IsNullOrWhiteSpace(record.TrustProfileVersion) &&
        !string.IsNullOrWhiteSpace(record.RequiredExtendedKeyUsageOid);
}
