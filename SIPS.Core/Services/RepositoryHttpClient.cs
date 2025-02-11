using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SIPS.Core.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Microsoft.Extensions.Logging;
namespace SIPS.Core.Services;

public class RepositoryHttpClient(ILogger<RepositoryHttpClient> logger, HttpClient httpClient) : IRepositoryHttpClient
{
    private readonly ILogger<RepositoryHttpClient> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public async Task<RepositoryResponse<T>> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, _httpClient.BaseAddress + url);
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // access token
            if (_httpClient.DefaultRequestHeaders.Authorization != null)
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _httpClient.DefaultRequestHeaders.Authorization?.Parameter);
            }
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GET request failed: {StatusCode}, URL: {Url}", response.StatusCode, url);
                return RepositoryResponse<T>.BadRequest("GET request failed");
            }
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<RepositoryResponse<T>>(content, serializerOptions);
            return data!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET request to {Url} failed.", url);
            return RepositoryResponse<T>.BadRequest(ex.Message);
        }
    }

    public async Task<RepositoryResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest content, CancellationToken cancellationToken = default)
    {
        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _httpClient.BaseAddress + url);
            requestMessage.Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json");
            // access token
            if (_httpClient.DefaultRequestHeaders.Authorization != null)
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _httpClient.DefaultRequestHeaders.Authorization?.Parameter);
            }
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("POST request failed: {StatusCode}, URL: {Url}", response.StatusCode, url);
                return RepositoryResponse<TResponse>.BadRequest("POST request failed");
            }
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            var data = JsonSerializer.Deserialize<RepositoryResponse<TResponse>>(responseContent, serializerOptions);
            return data!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST request to {Url} failed.", url);
            return RepositoryResponse<TResponse>.BadRequest(ex.Message);
        }
    }

    public void AddAuthHeaders(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
