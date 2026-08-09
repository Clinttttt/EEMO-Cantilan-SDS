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

            // Change tokens are taken BEFORE the factory runs, and this ordering is the whole correctness of
            // the cache.
            //
            // Taken afterwards, a write that committed while the factory was reading would be lost: the
            // invalidator cancels its region token AND removes it, so the next request for that token created a
            // fresh, uncancelled one. The value just read - from before the write - was then stored under it and
            // served for the full TTL. A payment recorded at the wrong moment would leave the dashboard showing
            // the old balance for minutes, with nothing able to evict it.
            //
            // Taken first, an invalidation during the factory cancels a token this entry already holds, so the
            // entry is expired the instant it is set. The caller still receives the value it read, which was
            // true when it was read; it simply is not handed to anybody else.
            var expirationTokens = regions
                .Distinct(StringComparer.Ordinal)
                .Select(invalidator.GetChangeToken)
                .ToList();

            var value = await factory(cancellationToken);
            if (value is null)
                return value;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
                Size = cacheOptions.EntrySize
            };

            foreach (var token in expirationTokens)
                options.AddExpirationToken(token);

            cache.Set(key, value, options);
            return value;
        }
        finally
        {
            // Keep the per-key semaphore in the map. Removing it here would let another thread that is
            // already blocked on this same instance proceed while a THIRD thread creates a fresh semaphore
            // for the same key — two factories then run concurrently, defeating the single-flight (anti-
            // stampede) guarantee. The retained semaphores are tiny and bounded by the set of distinct
            // cache keys (period/facility/tenant shaped), so retention is cheap.
            keyLock.Release();
        }
    }
}
