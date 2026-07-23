using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
namespace SIPS.Core.Services;

public class InterfaceHttpClient(ILogger<InterfaceHttpClient> logger, HttpClient httpClient, IOptions<CoreOptions> coreOptions) : IInterfaceHttpClient
{
    private readonly ILogger<InterfaceHttpClient> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly SemaphoreSlim _callbackTokenLock = new(1, 1);
    private string? _cachedCallbackToken;
    private DateTimeOffset _callbackTokenExpiresAt = DateTimeOffset.MinValue;
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public async Task<Response<JsonObject?>> Send(string completeUrl, Dictionary<string, string>? headers, StringContent requestContent, CancellationToken cancellationToken = default)
    {
        string? idemKey = null;
        string? txId = null;
        string? corrId = null;
        try
        {
            var sw = Stopwatch.StartNew();
            headers?.TryGetValue("X-Idempotency-Key", out idemKey);
            headers?.TryGetValue("X-Transaction-Id", out txId);
            headers?.TryGetValue("X-Correlation-Id", out corrId);
            _logger.LogInformation("HTTP POST start: {Url} idem={Idempotency} txId={TxId} corr={CorrelationId}", completeUrl, string.IsNullOrWhiteSpace(idemKey) ? "none" : idemKey, txId ?? "none", corrId ?? "none");
            HttpRequestMessage message = new(HttpMethod.Post, completeUrl)
            {
                RequestUri = new Uri(completeUrl),
                Content = requestContent,
                Headers =
                {
                }
            };

            // ensure content-type is correctly set on the content
            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(_core.HttpTimeoutSeconds > 0 ? _core.HttpTimeoutSeconds : 15));

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    message.Headers.Add(header.Key, header.Value);
                }
            }

            if (ShouldAttachCallbackBearer(headers))
            {
                var token = await GetCallbackBearerTokenAsync(linkedCts.Token);
                if (string.IsNullOrWhiteSpace(token))
                {
                    sw.Stop();
                    _logger.LogError("HTTP POST auth failed before callback: {Url} durationMs={Duration} corr={CorrelationId}", completeUrl, sw.ElapsedMilliseconds, corrId ?? "none");
                    return Response<JsonObject?>.Fail("Callback service-account token request failed", HttpStatusCode.Unauthorized);
                }

                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await _httpClient.SendAsync(message, linkedCts.Token);
            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
            var data = TryDeserializeJsonObject(content, completeUrl, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                sw.Stop();
                _logger.LogWarning("HTTP POST end: {Url} status={StatusCode} durationMs={Duration} idem={Idempotency} txId={TxId} corr={CorrelationId} data={data}", completeUrl, (int)response.StatusCode, sw.ElapsedMilliseconds, string.IsNullOrWhiteSpace(idemKey) ? "none" : idemKey, txId ?? "none", corrId ?? "none", data);
                return Response<JsonObject?>.Fail("Request Failed with Error", response.StatusCode, data);
            }

            sw.Stop();
            _logger.LogInformation("HTTP POST end: {Url} status={StatusCode} durationMs={Duration} idem={Idempotency} txId={TxId} corr={CorrelationId}", completeUrl, (int)response.StatusCode, sw.ElapsedMilliseconds, string.IsNullOrWhiteSpace(idemKey) ? "none" : idemKey, txId ?? "none", corrId ?? "none");
            return Response<JsonObject?>.Success(data);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "HTTP POST timeout: {Url} idem={Idempotency} txId={TxId} corr={CorrelationId}", completeUrl, idemKey ?? "none", txId ?? "none", corrId ?? "none");
            return Response<JsonObject?>.Fail("Request timed out", HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP POST failed: {Url} idem={Idempotency} txId={TxId} corr={CorrelationId}", completeUrl, idemKey ?? "none", txId ?? "none", corrId ?? "none");
            return Response<JsonObject?>.Fail(ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    public async Task<Response<string>> Send4XML(string url, StringContent requestContent, CancellationToken cancellationToken = default)
    {
        string? txId = null;
        string? corrId = null;
        try
        {
            var sw = Stopwatch.StartNew();
            HttpRequestMessage message = new(HttpMethod.Post, url)
            {
                RequestUri = new Uri(url),
                Content = requestContent,
                Headers =
                {
                }
            };

            requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(_core.HttpTimeoutSeconds > 0 ? _core.HttpTimeoutSeconds : 15));

            var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                sw.Stop();
                _logger.LogWarning("HTTP POST end: {Url} status={StatusCode} durationMs={Duration} txId={TxId} corr={CorrelationId} data={data}", url, (int)response.StatusCode, sw.ElapsedMilliseconds, txId ?? "none", corrId ?? "none", content);
                return Response<string>.Fail("Failed To Get Valid Response From SIPS", response.StatusCode, content);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                sw.Stop();
                _logger.LogWarning("HTTP POST end: {Url} status={StatusCode} durationMs={Duration} data={data}", url, (int)response.StatusCode, sw.ElapsedMilliseconds, content);
                return Response<string>.Fail("SIPS Responded with Bad Request - Check your ", response.StatusCode);
            }

            sw.Stop();
            _logger.LogInformation("HTTP POST end: {Url} status={StatusCode} durationMs={Duration} txId={TxId} corr={CorrelationId}", url, (int)response.StatusCode, sw.ElapsedMilliseconds, txId ?? "none", corrId ?? "none");
            return Response<string>.Success(content);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "HTTP POST timeout: {Url} txId={TxId} corr={CorrelationId}", url, txId ?? "none", corrId ?? "none");
            return Response<string>.Fail("Request timed out", HttpStatusCode.RequestTimeout, "Request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP POST failed: {Url} txId={TxId} corr={CorrelationId}", url, txId ?? "none", corrId ?? "none");
            return Response<string>.Fail(ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    private JsonObject? TryDeserializeJsonObject(string content, string url, HttpStatusCode statusCode)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("HTTP POST response body was empty: {Url} status={StatusCode}", url, (int)statusCode);
            return new JsonObject
            {
                ["message"] = "Empty response body",
                ["statusCode"] = (int)statusCode
            };
        }

        try
        {
            return JsonSerializer.Deserialize<JsonObject>(content, serializerOptions);
        }
        catch (JsonException ex)
        {
            var preview = content.Length > 512 ? content[..512] : content;
            _logger.LogWarning(ex, "HTTP POST response body was not valid JSON: {Url} status={StatusCode} body={Body}", url, (int)statusCode, preview);
            return new JsonObject
            {
                ["message"] = "Non-JSON response body",
                ["statusCode"] = (int)statusCode,
                ["body"] = preview
            };
        }
    }

    public void AddAuthHeaders(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private bool ShouldAttachCallbackBearer(Dictionary<string, string>? headers)
    {
        if (!_core.CallbackAuthEnabled)
        {
            return false;
        }

        return headers == null
            || !headers.Keys.Any(key => string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> GetCallbackBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_core.CallbackTokenEndpoint)
            || string.IsNullOrWhiteSpace(_core.CallbackClientId)
            || string.IsNullOrWhiteSpace(_core.CallbackClientSecret))
        {
            _logger.LogError("Callback service-account auth is enabled but token endpoint/client credentials are incomplete.");
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(_cachedCallbackToken)
            && now < _callbackTokenExpiresAt.AddSeconds(-30))
        {
            return _cachedCallbackToken;
        }

        await _callbackTokenLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(_cachedCallbackToken)
                && now < _callbackTokenExpiresAt.AddSeconds(-30))
            {
                return _cachedCallbackToken;
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _core.CallbackClientId,
                ["client_secret"] = _core.CallbackClientSecret
            };

            if (!string.IsNullOrWhiteSpace(_core.CallbackScope))
            {
                form["scope"] = _core.CallbackScope;
            }

            if (!string.IsNullOrWhiteSpace(_core.CallbackAudience))
            {
                form["audience"] = _core.CallbackAudience;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _core.CallbackTokenEndpoint)
            {
                Content = new FormUrlEncodedContent(form)
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Callback service-account token request failed with status {StatusCode}: {Content}", response.StatusCode, content);
                return null;
            }

            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            {
                _logger.LogError("Callback service-account token response did not include access_token.");
                return null;
            }

            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogError("Callback service-account token response included an empty access_token.");
                return null;
            }

            var expiresInSeconds = 300;
            if (document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                && expiresInElement.TryGetInt32(out var parsedExpiresIn)
                && parsedExpiresIn > 0)
            {
                expiresInSeconds = parsedExpiresIn;
            }

            _cachedCallbackToken = accessToken;
            _callbackTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
            _logger.LogInformation("Callback service-account token acquired for client {ClientId}; expires in {ExpiresIn}s.", _core.CallbackClientId, expiresInSeconds);
            return _cachedCallbackToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Callback service-account token request failed.");
            return null;
        }
        finally
        {
            _callbackTokenLock.Release();
        }
    }
}
