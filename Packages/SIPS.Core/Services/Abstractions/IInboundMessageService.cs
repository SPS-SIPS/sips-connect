using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SIPS.Core.Services.Abstractions;

public interface IInboundMessageService
{
    Task<(bool ok, TRequest? request)> VerifyAndParseAsync<TRequest>(
        string xml,
        Func<string, (bool ok, TRequest? request)> tryParse,
        CancellationToken ct,
        string correlationId);
}
