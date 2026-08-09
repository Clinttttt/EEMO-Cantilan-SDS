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

    [Fact]
    public async Task AWriteThatLandsWhileTheFactoryIsReading_IsNotLostForTheWholeTtl()
    {
        // The race that could pin stale money on screen for minutes.
        //
        // The factory reads the database. If a payment is recorded and its region invalidated DURING that read,
        // the value being returned is already out of date. Change tokens used to be taken after the factory, and
        // the invalidator cancels its token AND removes it - so the token taken afterwards was a fresh,
        // uncancelled one, and the stale figure was cached under it and served until the TTL ran out. Nothing
        // could evict it, because the invalidation had already happened.
        var options = new EemoCacheOptions();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
        var invalidator = new MemoryEemoCacheInvalidator();
        var cache = new MemoryEemoAppCache(memoryCache, invalidator, options);
        var region = EemoCacheRegions.Period("tenant", 2026, 8);

        var factoryCalls = 0;

        // Stands for "a collector recorded a payment while this report was being built".
        async Task<int> ReadThenSomebodyPays(CancellationToken _)
        {
            factoryCalls++;
            var figureBeforeThePayment = 1_000;
            await invalidator.InvalidateRegionAsync(region);
            return figureBeforeThePayment;
        }

        var first = await cache.GetOrCreateAsync("balance", new[] { region }, TimeSpan.FromMinutes(5), ReadThenSomebodyPays);

        // The caller still gets what it read - that was true at the moment it read it.
        Assert.Equal(1_000, first);
        Assert.Equal(1, factoryCalls);

        // But it must not have been kept: the next reader has to go back to the database, where the payment now is.
        var second = await cache.GetOrCreateAsync("balance", new[] { region }, TimeSpan.FromMinutes(5),
            _ => Task.FromResult(750));

        Assert.Equal(750, second);
        Assert.Equal(1, factoryCalls);   // the second call used its own factory, not the cached 1,000
    }

    [Fact]
    public async Task AnUndisturbedValueIsStillCached()
    {
        // The counterpart: taking the tokens earlier must not make the cache useless. With no invalidation in
        // flight, one read still serves the next caller.
        var options = new EemoCacheOptions();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
        var invalidator = new MemoryEemoCacheInvalidator();
        var cache = new MemoryEemoAppCache(memoryCache, invalidator, options);
        var region = EemoCacheRegions.Period("tenant", 2026, 9);
        var factoryCalls = 0;

        Task<int> Factory(CancellationToken _)
        {
            factoryCalls++;
            return Task.FromResult(4_200);
        }

        Assert.Equal(4_200, await cache.GetOrCreateAsync("k", new[] { region }, TimeSpan.FromMinutes(5), Factory));
        Assert.Equal(4_200, await cache.GetOrCreateAsync("k", new[] { region }, TimeSpan.FromMinutes(5), Factory));
        Assert.Equal(1, factoryCalls);
    }

}
