namespace SIPS.XMLDsig.Xades.Interfaces;
public interface INativeVerifier
{
    Task<(bool result, VerboseResult verbose)> VerifySignature(string message, bool checkOwnerShip, CancellationToken cancellationToken);
    Task<SignatureVerificationResult> VerifyWithProvenance(string message, CancellationToken cancellationToken);
}

public sealed record VerifiedSignerProvenance(string Owner, string Authority, string Environment, string RepresentedParticipant, string IssuerDn, string SerialNumber, string CertificateSha256, bool FromOwnershipVerified, string TrustProfileVersion, string MessageDefinitionId, string BusinessService, string ProtectedContentSha256, DateTimeOffset VerifiedAt);
public sealed record SignatureVerificationResult(bool Result, VerboseResult Verbose, VerifiedSignerProvenance? Signer);
