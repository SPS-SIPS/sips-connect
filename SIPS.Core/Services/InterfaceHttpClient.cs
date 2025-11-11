using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using Microsoft.Extensions.Logging;
namespace SIPS.Core.Services;

public class InterfaceHttpClient(ILogger<InterfaceHttpClient> logger, HttpClient httpClient) : IInterfaceHttpClient
{
    private readonly ILogger<InterfaceHttpClient> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;
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

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    message.Headers.Add(header.Key, header.Value);
                }
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _httpClient.SendAsync(message, linkedCts.Token);
            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
            var data = JsonSerializer.Deserialize<JsonObject>(content, serializerOptions);

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
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "HTTP POST timeout: {Url} idem={Idempotency} txId={TxId} corr={CorrelationId}", completeUrl, "timeout", "timeout", "timeout");
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
            linkedCts.CancelAfter(TimeSpan.FromSeconds(15));

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
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "HTTP POST timeout: {Url} txId={TxId} corr={CorrelationId}", url, "timeout", "timeout");
            return Response<string>.Fail("Request timed out", HttpStatusCode.RequestTimeout, "Request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP POST failed: {Url} txId={TxId} corr={CorrelationId}", url, txId ?? "none", corrId ?? "none");
            return Response<string>.Fail(ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    public void AddAuthHeaders(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
