using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace EEMOCantilanSDS.Testing.Infrastructure.Caching;

public class MemoryEemoAppCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_HitsCache_UntilRegionIsInvalidated()
    {
        var options = new EemoCacheOptions();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
        var invalidator = new MemoryEemoCacheInvalidator();
        var cache = new MemoryEemoAppCache(memoryCache, invalidator, options);
        var region = EemoCacheRegions.Period("tenant", 2026, 6);
        var factoryCalls = 0;

        Task<int> Factory(CancellationToken _)
        {
            factoryCalls++;
            return Task.FromResult(factoryCalls);
        }

        var first = await cache.GetOrCreateAsync("key", new[] { region }, TimeSpan.FromMinutes(5), Factory);
        var second = await cache.GetOrCreateAsync("key", new[] { region }, TimeSpan.FromMinutes(5), Factory);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, factoryCalls);

        await invalidator.InvalidateRegionAsync(region);
        var afterInvalidation = await cache.GetOrCreateAsync("key", new[] { region }, TimeSpan.FromMinutes(5), Factory);

        Assert.Equal(2, afterInvalidation);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_CoalescesConcurrentMisses_ForSameKey()
    {
        var options = new EemoCacheOptions();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
        var invalidator = new MemoryEemoCacheInvalidator();
        var cache = new MemoryEemoAppCache(memoryCache, invalidator, options);
        var region = EemoCacheRegions.Period("tenant", 2026, 6);
        var factoryCalls = 0;

        async Task<int> Factory(CancellationToken ct)
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(50, ct);
            return 42;
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => cache.GetOrCreateAsync("same-key", new[] { region }, TimeSpan.FromMinutes(5), Factory)));

        Assert.All(results, value => Assert.Equal(42, value));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_SurvivesRepeatedInvalidation_AfterSourceDisposed()
    {
        // Each InvalidateRegionAsync now cancels AND disposes the region's token source; GetChangeToken
        // must create a fresh source afterwards. Cycling several times proves the dispose + recreate path
        // is stable (no ObjectDisposedException, correct re-caching each round).
        var options = new EemoCacheOptions();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
        var invalidator = new MemoryEemoCacheInvalidator();
        var cache = new MemoryEemoAppCache(memoryCache, invalidator, options);
        var region = EemoCacheRegions.Period("tenant", 2026, 6);
        var factoryCalls = 0;

        Task<int> Factory(CancellationToken _)
        {
            factoryCalls++;
            return Task.FromResult(factoryCalls);
        }

        for (var cycle = 1; cycle <= 4; cycle++)
        {
            var cached = await cache.GetOrCreateAsync("key", new[] { region }, TimeSpan.FromMinutes(5), Factory);
            Assert.Equal(cycle, cached);                 // fresh value produced this cycle
            var hit = await cache.GetOrCreateAsync("key", new[] { region }, TimeSpan.FromMinutes(5), Factory);
            Assert.Equal(cycle, hit);                    // served from cache (factory not re-run)
            await invalidator.InvalidateRegionAsync(region);   // cancel + dispose the source
        }

        Assert.Equal(4, factoryCalls);                   // exactly one factory run per cycle
    }
}
