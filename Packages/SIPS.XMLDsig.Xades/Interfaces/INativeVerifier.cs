namespace SIPS.XMLDsig.Xades.Interfaces;
public interface INativeVerifier
{
    Task<(bool result, VerboseResult verbose)> VerifySignature(string message, bool checkOwnerShip, CancellationToken cancellationToken);
}
