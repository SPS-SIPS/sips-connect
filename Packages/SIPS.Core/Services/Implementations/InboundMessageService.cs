using System;
using System.Threading;
using System.Threading.Tasks;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Verification;

namespace SIPS.Core.Services.Implementations;

public sealed class InboundMessageService(ISignatureService signature) : IInboundMessageService
{
    private readonly ISignatureService _signature = signature;

    public async Task<(bool ok, TRequest? request)> VerifyAndParseAsync<TRequest>(
        string xml,
        Func<string, (bool ok, TRequest? request)> tryParse,
        CancellationToken ct,
        string correlationId)
    {
        var (ok, _) = await _signature.VerifyAsync(xml, ct);
        if (!ok)
            return (false, default);

        var result = tryParse(xml);
        return result;
    }
}
