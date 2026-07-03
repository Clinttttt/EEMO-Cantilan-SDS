using System.Collections.Concurrent;
using EEMOCantilanSDS.Application.Common.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace EEMOCantilanSDS.Infrastructure.Caching;

public sealed class MemoryEemoAppCache(
    IMemoryCache cache,
    MemoryEemoCacheInvalidator invalidator,
    EemoCacheOptions cacheOptions) : IEemoAppCache
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> keyLocks = new(StringComparer.Ordinal);

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
        var keyLock = keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out cached) && cached is not null)
                return cached;

            var value = await factory(cancellationToken);
            if (value is null)
                return value;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
                Size = cacheOptions.EntrySize
            };

            foreach (var region in regions.Distinct(StringComparer.Ordinal))
                options.AddExpirationToken(invalidator.GetChangeToken(region));

            cache.Set(key, value, options);
            return value;
        }
        finally
        {
            keyLock.Release();
            keyLocks.TryRemove(key, out _);
        }
    }
}
