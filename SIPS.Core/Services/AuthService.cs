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
        await _cacheService.SetAsync($"auth:login:{username}", response, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(response!.Data.ExpiresIn)
        }, cancellationToken);

        // important: to add the access token to the http service
        _httpService.AddAuthHeaders(response!.Data.AccessToken);

        return (response.Data, null);
    }

    private async Task<LoginResponse?> GetTokenAsync(string username, CancellationToken cancellationToken = default)
    {
        var response = await _cacheService.GetAsync<LoginResponse>($"auth:login:{username}", cancellationToken);
        if (response == null)
        {
            _logger.LogWarning("Login response not found in cache.");
            return null;
        }

        // Check if the token is expired
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > response.ExpiresIn)
        {
            _logger.LogWarning("Login response expired.");
            return null;
        }

        return response;
    }
}