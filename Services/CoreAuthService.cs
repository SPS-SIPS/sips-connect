using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SIPS.Connect.Services;

public interface ICoreAuthService
{
    Task<string?> GetAuthTokenAsync(CancellationToken cancellationToken = default);
}

public class CoreAuthService : ICoreAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CoreAuthService> _logger;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public CoreAuthService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CoreAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> GetAuthTokenAsync(CancellationToken cancellationToken = default)
    {
        // Return cached token if still valid
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
        {
            _logger.LogDebug("Returning cached authentication token");
            return _cachedToken;
        }

        try
        {
            var baseUrl = _configuration["Core:BaseUrl"];
            var loginEndpoint = _configuration["Core:LoginEndpoint"];
            var username = _configuration["Core:Username"];
            var password = _configuration["Core:Password"];

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(loginEndpoint) || 
                string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogError("Missing Core authentication configuration");
                return null;
            }

            var loginUrl = $"{baseUrl}{loginEndpoint}";
            _logger.LogInformation("Authenticating with Core API at {LoginUrl}", loginUrl);

            var client = _httpClientFactory.CreateClient();
            var loginRequest = new
            {
                username = username,
                password = password
            };

            var json = JsonSerializer.Serialize(loginRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(loginUrl, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Authentication failed with status {StatusCode}: {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent, options);
            
            if (loginResponse?.Data?.AccessToken != null)
            {
                _cachedToken = loginResponse.Data.AccessToken;
                // Cache token for 15 minutes (tokens typically expire in 20 minutes)
                _tokenExpiry = DateTime.UtcNow.AddMinutes(15);
                _logger.LogInformation("Successfully authenticated with Core API");
                return _cachedToken;
            }

            _logger.LogWarning("Authentication response did not contain access token");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating with Core API");
            return null;
        }
    }
}

public class LoginResponse
{
    public LoginData? Data { get; set; }
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public string? Message { get; set; }
}

public class LoginData
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
