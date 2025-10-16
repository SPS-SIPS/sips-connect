using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Models.DTOs;
using SIPS.Core.Interfaces;

namespace SIPS.Core.Services.Callback;

public interface ICallbackClient
{
    Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null);
}

public sealed class CallbackClient(IInterfaceHttpClient httpClient, ILogger<CallbackClient> logger, ICorrelationService correlation) : ICallbackClient
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<CallbackClient> _logger = logger;
    private readonly ICorrelationService _correlation = correlation;

    public async Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null)
    {
        var cid = correlationId ?? _correlation.Create();
        _logger.LogInformation("[{CorrelationId}] Sending callback to {Url}", cid, url);
        var response = await _httpClient.Send(url, headers, content, ct);
        _logger.LogInformation("[{CorrelationId}] Callback status: {StatusCode}", cid, response.StatusCode);
        return response;
    }
}
