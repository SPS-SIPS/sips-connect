using Microsoft.Extensions.Caching.Distributed;
using SIPS.Connect.Models;
using SIPS.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SIPS.Connect.Services;

public interface ILiveParticipantsService
{
    Task<List<ParticipantStatus>> GetLiveParticipantsAsync(bool? isLive = null, CancellationToken cancellationToken = default);
    Task<bool> IsParticipantLiveAsync(string bic, CancellationToken cancellationToken = default);
    Task<List<string>> GetAvailableParticipantBicsAsync(CancellationToken cancellationToken = default);
}

public class LiveParticipantsService : ILiveParticipantsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<LiveParticipantsService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ICoreAuthService _authService;
    private readonly int _cacheDurationMinutes;
    private readonly string _liveParticipantsEndpoint;
    private readonly string _baseUrl;
    private const string CacheKey = "live_participants_cache";

    public LiveParticipantsService(
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        ILogger<LiveParticipantsService> logger,
        IConfiguration configuration,
        ICoreAuthService authService)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _configuration = configuration;
        _authService = authService;
        _cacheDurationMinutes = _configuration.GetValue<int>("LiveParticipants:CacheDurationMinutes", 5);
        _liveParticipantsEndpoint = _configuration["Core:LiveParticipantsEndpoint"] ?? "/v1/participants/live";
        _baseUrl = _configuration["Core:BaseUrl"] ?? "http://localhost:5004/api";
    }

    public async Task<List<ParticipantStatus>> GetLiveParticipantsAsync(bool? isLive = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var cachedData = await GetFromCacheAsync(cancellationToken);
            
            if (cachedData != null)
            {
                _logger.LogDebug("Returning cached live participants data");
                return FilterByStatus(cachedData, isLive);
            }

            _logger.LogInformation("Cache miss - fetching live participants from API");
            var freshData = await FetchFromApiAsync(cancellationToken);
            
            if (freshData != null && freshData.Any())
            {
                await SaveToCacheAsync(freshData, cancellationToken);
                return FilterByStatus(freshData, isLive);
            }

            _logger.LogWarning("No live participants data available from API");
            return new List<ParticipantStatus>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching live participants");
            throw;
        }
    }

    public async Task<bool> IsParticipantLiveAsync(string bic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bic))
        {
            throw new ArgumentException("BIC cannot be null or empty", nameof(bic));
        }

        var liveParticipants = await GetLiveParticipantsAsync(isLive: true, cancellationToken);
        return liveParticipants.Any(p => p.InstitutionBic.Equals(bic, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<string>> GetAvailableParticipantBicsAsync(CancellationToken cancellationToken = default)
    {
        var liveParticipants = await GetLiveParticipantsAsync(isLive: true, cancellationToken);
        return liveParticipants.Select(p => p.InstitutionBic).ToList();
    }

    private async Task<List<ParticipantStatus>?> GetFromCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cachedBytes = await _cache.GetAsync(CacheKey, cancellationToken);
            
            if (cachedBytes == null || cachedBytes.Length == 0)
            {
                return null;
            }

            var cachedJson = System.Text.Encoding.UTF8.GetString(cachedBytes);
            var cachedData = JsonSerializer.Deserialize<List<ParticipantStatus>>(cachedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return cachedData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading from cache, will fetch fresh data");
            return null;
        }
    }

    private async Task SaveToCacheAsync(List<ParticipantStatus> data, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheDurationMinutes)
            };

            await _cache.SetAsync(CacheKey, bytes, cacheOptions, cancellationToken);
            _logger.LogInformation("Cached live participants data for {Minutes} minutes", _cacheDurationMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error saving to cache, continuing without cache");
        }
    }

    private async Task<List<ParticipantStatus>?> FetchFromApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get authentication token
            var token = await _authService.GetAuthTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Failed to obtain authentication token");
                return null;
            }

            // Create HTTP request with Bearer token
            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{_liveParticipantsEndpoint}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var httpResponse = await client.SendAsync(request, cancellationToken);
            
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Request failed with status {StatusCode}. Response: {Response}", 
                    httpResponse.StatusCode, errorContent);
                return null;
            }

            var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<ParticipantStatus>>>(responseContent, options);
            
            if (apiResponse?.Data != null && apiResponse.Data.Any())
            {
                _logger.LogInformation("Successfully fetched {Count} participants from API", apiResponse.Data.Count);
                return apiResponse.Data;
            }

            _logger.LogWarning("API returned unsuccessful response or no data");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching live participants from API");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching live participants from API");
            throw;
        }
    }

    private List<ParticipantStatus> FilterByStatus(List<ParticipantStatus> participants, bool? isLive)
    {
        if (isLive == null)
        {
            return participants;
        }

        return participants.Where(p => p.IsLive == isLive.Value).ToList();
    }
}
