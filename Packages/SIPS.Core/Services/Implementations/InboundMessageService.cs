using System;
using System.Threading;
using System.Threading.Tasks;
using SIPS.Core.Interfaces;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Verification;

namespace SIPS.Core.Services.Implementations;

/// <summary>
/// Verifies the (IPS vendor legacy profile) signature of an inbound message and parses it.
/// When the hosting application already authenticated the message (the PAPSS callback guard verified
/// the WP-SIPS XAdES profile, signer provenance and local destination), the legacy verification is
/// skipped: a WP-SIPS-signed message never satisfies the legacy profile, so re-verifying it would reject
/// every PAPSS pacs.008/pacs.004/pacs.028/pacs.002 after the guard accepted it.
/// </summary>
public sealed class InboundMessageService(ISignatureService signature, IInboundAuthenticationContext? authentication = null) : IInboundMessageService
{
    private readonly ISignatureService _signature = signature;
    private readonly IInboundAuthenticationContext _authentication = authentication ?? new NoInboundPreAuthentication();

    public async Task<(bool ok, TRequest? request)> VerifyAndParseAsync<TRequest>(
        string xml,
        Func<string, (bool ok, TRequest? request)> tryParse,
        CancellationToken ct,
        string correlationId)
    {
        if (!_authentication.IsPreAuthenticated)
        {
            var (ok, _) = await _signature.VerifyAsync(xml, ct);
            if (!ok)
                return (false, default);
        }

        var result = tryParse(xml);
        return result;
    }
}
