using SIPS.Core.Interfaces;
using SIPS.Core.Models;
using SIPS.Core.Options;
using Microsoft.Extensions.Logging;

namespace SIPS.Core.Services;
public class AuthService(CoreOptions options, ILogger<AuthService> logger, IRepositoryHttpClient httpService, ICacheService cacheService) : IAuthService
{
    private readonly ILogger<AuthService> _logger = logger;
    private readonly IRepositoryHttpClient _httpService = httpService;
    private readonly CoreOptions _configuration = options;
    private readonly ICacheService _cacheService = cacheService;

    public async Task<(LoginResponse? Token, string? Error)> LoginAsync(CancellationToken cancellationToken = default)
    {
        var url = _configuration.LoginEndpoint;
        var username = _configuration.Username;
        var password = _configuration.Password;

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _logger.LogError("Invalid configuration for login.");
            return (null, "Invalid configuration for login.");
        }

        // Check if the token is already cached and not expired
        var token = await GetTokenAsync(username, cancellationToken);
        if (token != null)
        {
            return (token, null);
        }

        LoginRequest request = new(username!, password!);
        var response = await _httpService.PostAsync<LoginRequest, LoginResponse>(url!, request, cancellationToken);
        if (!response.IsSuccess)
        {
            _logger.LogError("Login failed: {Error}", response.Message);
            return (null, response.Message);
        }

        // Cache the login response for future use
        // [FIX]: standard OAuth2 expires_in is in seconds, not minutes. 
        // We also deduct a small buffer (30s) to avoid race conditions.
        var bufferSeconds = 30;
        var effectiveExpiresIn = Math.Max(0, response.Data.ExpiresIn - bufferSeconds);

        await _cacheService.SetAsync($"auth:login:{username}", response.Data, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(effectiveExpiresIn)
        }, cancellationToken);

        // important: to add the access token to the http service
        _httpService.AddAuthHeaders(response!.Data.AccessToken);

        return (response.Data, null);
    }

    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        var username = _configuration.Username;
        if (!string.IsNullOrEmpty(username))
        {
            _logger.LogInformation("Clearing auth cache for user {Username} due to 401/Invalidation request.", username);
            await _cacheService.RemoveAsync($"auth:login:{username}", cancellationToken);
        }
    }

    private async Task<LoginResponse?> GetTokenAsync(string username, CancellationToken cancellationToken = default)
    {
        var response = await _cacheService.GetAsync<LoginResponse>($"auth:login:{username}", cancellationToken);
        if (response == null)
        {
            _logger.LogDebug("Login response not found in cache for user {Username}.", username);
            return null;
        }

        // Note: AbsoluteExpirationRelativeToNow in SetAsync handles expiration at the cache layer.
        // If we retrieve it from cache, it's generally still valid.
        return response;
    }
}
