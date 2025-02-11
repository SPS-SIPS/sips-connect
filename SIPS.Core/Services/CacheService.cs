using System.Text.Json;
using SIPS.Core.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace SIPS.Core.Services;
public sealed class CacheService(IDistributedCache cache, ILogger<CacheService> logger) : ICacheService
{
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<CacheService> _logger = logger;

    public async Task<T?> GetAsync<T>(string key, CancellationToken token = default)
    {
        var value = await _cache.GetStringAsync(key, token);
        if (value is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value);
    }

    public async Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default)
    {
        var serialized = JsonSerializer.Serialize(value);
        await _cache.SetStringAsync(key, serialized, options ?? new DistributedCacheEntryOptions(), token);
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await _cache.RemoveAsync(key, token);
    }
}