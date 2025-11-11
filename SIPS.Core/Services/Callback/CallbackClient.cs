using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Models.DTOs;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;

namespace SIPS.Core.Services.Callback;

public interface ICallbackClient
{
    Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null);
}

public sealed class CallbackClient(IInterfaceHttpClient httpClient, ILogger<CallbackClient> logger, ICorrelationService correlation, IOptions<CoreOptions> coreOptions) : ICallbackClient
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<CallbackClient> _logger = logger;
    private readonly ICorrelationService _correlation = correlation;
    private readonly CoreOptions _core = coreOptions.Value;

    public async Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null)
    {
        var cid = correlationId ?? _correlation.Create();
        if (!headers.ContainsKey("X-Correlation-Id"))
            headers["X-Correlation-Id"] = cid;
        if (!_core.IncludeIdempotencyHeaders && headers.ContainsKey("X-Idempotency-Key"))
            headers.Remove("X-Idempotency-Key");
        _logger.LogInformation("[{CorrelationId}] Sending callback to {Url}", cid, url);
        var response = await _httpClient.Send(url, headers, content, ct);
        _logger.LogInformation("[{CorrelationId}] Callback status: {StatusCode}", cid, response.StatusCode);
        return response;
    }
}
