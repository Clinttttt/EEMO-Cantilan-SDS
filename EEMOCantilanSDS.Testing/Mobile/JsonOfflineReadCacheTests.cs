using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class JsonOfflineReadCacheTests : IDisposable
{
    private sealed record Sample(string Name, int Value);

    private readonly string _dir;

    public JsonOfflineReadCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eemo-readcache-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Set_then_Get_round_trips_the_value()
    {
        var cache = new JsonOfflineReadCache(_dir);

        await cache.SetAsync("k", new Sample("Ana", 30));

        Assert.Equal(new Sample("Ana", 30), await cache.GetAsync<Sample>("k"));
    }

    [Fact]
    public async Task Value_persists_to_disk_and_reloads_in_a_fresh_instance()
    {
        await new JsonOfflineReadCache(_dir).SetAsync("k", new Sample("Bert", 72));

        // Cold start: a brand-new instance must read it back from disk.
        var reloaded = await new JsonOfflineReadCache(_dir).GetAsync<Sample>("k");

        Assert.Equal(new Sample("Bert", 72), reloaded);
    }

    [Fact]
    public async Task Get_missing_key_returns_default()
    {
        var cache = new JsonOfflineReadCache(_dir);

        Assert.Null(await cache.GetAsync<Sample>("nope"));
    }

    [Fact]
    public async Task Set_overwrites_the_previous_value()
    {
        var cache = new JsonOfflineReadCache(_dir);

        await cache.SetAsync("k", new Sample("old", 1));
        await cache.SetAsync("k", new Sample("new", 2));

        Assert.Equal(new Sample("new", 2), await cache.GetAsync<Sample>("k"));
    }

    [Fact]
    public async Task ClearAsync_removes_every_entry()
    {
        var cache = new JsonOfflineReadCache(_dir);
        await cache.SetAsync("a", new Sample("x", 1));
        await cache.SetAsync("b", new Sample("y", 2));

        await cache.ClearAsync();

        Assert.Null(await cache.GetAsync<Sample>("a"));
        Assert.Null(await cache.GetAsync<Sample>("b"));
    }

    [Fact]
    public async Task RemoveByPrefixAsync_removes_only_matching_keys()
    {
        var cache = new JsonOfflineReadCache(_dir);
        await cache.SetAsync("menu", new Sample("m", 0));
        await cache.SetAsync("monthly|TCC|2026|6", new Sample("c", 1));
        await cache.SetAsync("records|all", new Sample("r", 2));

        await cache.RemoveByPrefixAsync("monthly", "records");

        Assert.NotNull(await cache.GetAsync<Sample>("menu"));          // preserved
        Assert.Null(await cache.GetAsync<Sample>("monthly|TCC|2026|6"));
        Assert.Null(await cache.GetAsync<Sample>("records|all"));
    }
}
