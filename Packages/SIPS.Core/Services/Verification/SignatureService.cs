using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPS.XMLDsig.Xades.Interfaces;

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
        var (result, verbose) = await _verifier.VerifySignature(message, false, ct);
        if (!result)
        {
            _logger.LogError("Signature verification failed: {Verbose}", verbose);
        }
        return (result, verbose?.SignatureStatus);
    }
}
