namespace SIPS.XMLDsig.Xades.Models;
public record CertificateDownloadResponse(string Content, string Owner, bool Revoked = false, string? Authority = null, string? Environment = null, string? RepresentedParticipant = null, string? CertificateSha256 = null, string? TrustProfileVersion = null, string? RequiredExtendedKeyUsageOid = null);
