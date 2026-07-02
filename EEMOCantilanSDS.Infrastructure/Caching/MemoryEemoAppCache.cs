using EEMOCantilanSDS.Application.Common.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace EEMOCantilanSDS.Infrastructure.Caching;

public sealed class MemoryEemoAppCache(
    IMemoryCache cache,
    MemoryEemoCacheInvalidator invalidator) : IEemoAppCache
{
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        IReadOnlyCollection<string> regions,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        cancellationToken.ThrowIfCancellationRequested();
        var value = await factory(cancellationToken);
        if (value is null)
            return value;

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        foreach (var region in regions.Distinct(StringComparer.Ordinal))
            options.AddExpirationToken(invalidator.GetChangeToken(region));

        cache.Set(key, value, options);
        return value;
    }
}
