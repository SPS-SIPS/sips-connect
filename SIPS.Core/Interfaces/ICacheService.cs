using Microsoft.Extensions.Caching.Distributed;

namespace SIPS.Core.Interfaces;
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken token = default);
    Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default);
    Task RemoveAsync(string key, CancellationToken token = default);
}
