using System.Net;
using System.Net.Http.Headers;
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
        try
        {
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
                _logger.LogWarning("POST request failed: {StatusCode}, URL: {Url} data: {data}", response.StatusCode, completeUrl, data);
                return Response<JsonObject?>.Fail("Request Failed with Error", response.StatusCode, data);
            }

            return Response<JsonObject?>.Success(data);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "POST request to {Url} timed out.", completeUrl);
            return Response<JsonObject?>.Fail("Request timed out", HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST request to {Url} failed.", completeUrl);
            return Response<JsonObject?>.Fail(ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    public async Task<Response<string>> Send4XML(string url, StringContent requestContent, CancellationToken cancellationToken = default)
    {
        try
        {
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
                _logger.LogWarning("POST request failed: {StatusCode}, URL: {Url} data: {data}", response.StatusCode, url, content);
                return Response<string>.Fail("Failed To Get Valid Response From SIPS", response.StatusCode, content);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning("POST request failed: {StatusCode}, URL: {Url} data: {data}", response.StatusCode, url, content);
                return Response<string>.Fail("SIPS Responded with Bad Request - Check your ", response.StatusCode);
            }

            return Response<string>.Success(content);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "POST request to {Url} timed out.", url);
            return Response<string>.Fail("Request timed out", HttpStatusCode.RequestTimeout, "Request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST request to {Url} failed.", url);
            return Response<string>.Fail(ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    public void AddAuthHeaders(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
