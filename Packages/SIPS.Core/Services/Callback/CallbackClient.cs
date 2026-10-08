using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Models.DTOs;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;

namespace SIPS.Core.Services.Callback;

public interface ICallbackClient
{
    /// <param name="bypassParticipantBinding">
    /// True for a call that must reach the exact <paramref name="url"/> it was given - a question asked of the
    /// bank's own corebank (e.g. CB_Verify) - rather than the PAPSS participant's configured bank-notification
    /// CallbackUrl. False (the default) preserves the existing behaviour: delivering an outcome/notification to
    /// the participant bank's own callback endpoint, which ParticipantCallbackClient resolves from the active
    /// PAPSS participant binding when one is present.
    /// </param>
    Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null, bool bypassParticipantBinding = false);
}

// Backward-compatible constructor for tests and older code paths
public sealed partial class CallbackClient
{
    public CallbackClient(IInterfaceHttpClient httpClient, ILogger<CallbackClient> logger, ICorrelationService correlation)
        : this(httpClient, logger, correlation, Microsoft.Extensions.Options.Options.Create(new CoreOptions()))
    { }
}

public sealed partial class CallbackClient(IInterfaceHttpClient httpClient, ILogger<CallbackClient> logger, ICorrelationService correlation, IOptions<CoreOptions> coreOptions) : ICallbackClient
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<CallbackClient> _logger = logger;
    private readonly ICorrelationService _correlation = correlation;
    private readonly CoreOptions _core = coreOptions.Value;

    public async Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null, bool bypassParticipantBinding = false)
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
