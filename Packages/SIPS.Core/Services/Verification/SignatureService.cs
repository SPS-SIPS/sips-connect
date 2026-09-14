using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Core.Services.Verification;

public interface ISignatureService
{
    Task<(bool ok, string? verbose)> VerifyAsync(string message, CancellationToken ct);
}

public sealed class SignatureService(INativeVerifier verifier, ILogger<SignatureService> logger) : ISignatureService
{
    private readonly INativeVerifier _verifier = verifier;
    private readonly ILogger<SignatureService> _logger = logger;

    public async Task<(bool ok, string? verbose)> VerifyAsync(string message, CancellationToken ct)
    {
        // SmartVista/IPS uses the vendor legacy signature profile. The shared IPS
        // certificate authenticates the signature, but is not bound to one BAH
        // sender. Participant ownership remains enforced by the PAPSS profile.
        var (result, verbose) = await _verifier.VerifySignature(
            message,
            checkOwnerShip: false,
            XadesProfile.IpsVendorLegacy,
            ct);
        if (!result)
        {
            var responseSha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();

            _logger.LogError(
                "IPS signature verification failed. CertificateStatus={CertificateStatus}; SignatureStatus={SignatureStatus}; ReferencesStatus={ReferencesStatus}; OwnershipStatus={OwnershipStatus}; ResponseLength={ResponseLength}; ResponseSha256={ResponseSha256}",
                verbose?.CertificateStatus,
                verbose?.SignatureStatus,
                verbose?.ReferencesStatus,
                verbose?.OwnershSIPStatus,
                message.Length,
                responseSha256);
            _logger.LogError(
                "IPS signature verification failed for exact response XML: {ResponseXml}",
                message);
        }
        return (result, verbose?.SignatureStatus);
    }
}
