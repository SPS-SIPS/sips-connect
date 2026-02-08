using SIPS.Connect.Models;
using SIPS.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SIPS.Connect.Services;

public class BalanceMonitoringService : IBalanceMonitoringService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BalanceMonitoringService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICoreAuthService _authService;
    private readonly string _balanceEndpoint;
    private readonly string _baseUrl;

    public BalanceMonitoringService(
        IHttpClientFactory httpClientFactory,
        ILogger<BalanceMonitoringService> logger,
        IConfiguration configuration,
        ICoreAuthService authService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _authService = authService;
        _balanceEndpoint = _configuration["Core:BalanceStatusEndpoint"] ?? "/v1/participants/balance-status";
        _baseUrl = _configuration["Core:BaseUrl"] ?? "http://localhost:5004/api";
    }

    public async Task<BalanceStatus?> GetBalanceStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching balance status from API");
            _logger.LogInformation("Balance endpoint: {Endpoint}", _balanceEndpoint);
            
            // Get authentication token
            var token = await _authService.GetAuthTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Failed to obtain authentication token");
                return null;
            }

            _logger.LogInformation("Authentication token obtained successfully");
            
            // Create HTTP request with Bearer token
            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{_balanceEndpoint}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            _logger.LogInformation("Sending GET request to {Url}", $"{_baseUrl}{_balanceEndpoint}");
            
            var httpResponse = await client.SendAsync(request, cancellationToken);
            
            _logger.LogInformation("Response received - StatusCode: {StatusCode}", httpResponse.StatusCode);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Request failed with status {StatusCode}. Response: {Response}", 
                    httpResponse.StatusCode, errorContent);
                return null;
            }

            var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Response content: {Content}", responseContent);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<BalanceStatus>>(responseContent, options);
            
            if (apiResponse?.Data != null)
            {
                _logger.LogInformation(
                    "Successfully fetched balance status - BIC: {Bic}, Zone: {Zone}, Balance: {Balance}", 
                    apiResponse.Data.Bic, 
                    apiResponse.Data.CurrentZone, 
                    apiResponse.Data.LastKnownBalance);
                return apiResponse.Data;
            }

            _logger.LogWarning("API returned no data");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching balance status from API");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching balance status from API");
            throw;
        }
    }
}

public class ApiResponse<T>
{
    public T? Data { get; set; }
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public string? Message { get; set; }
}
