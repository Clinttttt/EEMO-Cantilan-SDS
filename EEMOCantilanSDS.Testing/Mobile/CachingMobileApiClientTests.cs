using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Services;
using Moq;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class CachingMobileApiClientTests
{
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);
    private const string RecordsKey = "records|all|2026-06-01|2026-06-30";

    private static IReadOnlyList<MobileCollectorRecordDto> Records() => new List<MobileCollectorRecordDto>();

    private static CachingMobileApiClient Sut(IMobileApiClient inner, IOfflineReadCache cache, bool online) =>
        new(inner, cache, new FakeConnectivityMonitor(online));

    [Fact]
    public async Task Online_successful_read_is_returned_and_cached()
    {
        var inner = new Mock<IMobileApiClient>();
        var fresh = Records();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success(fresh));
        var cache = new FakeOfflineReadCache();
        var sut = Sut(inner.Object, cache, online: true);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.True(result.IsSuccess);
        Assert.Same(fresh, result.Value);
        Assert.Same(fresh, await cache.GetAsync<IReadOnlyList<MobileCollectorRecordDto>>(RecordsKey));
    }

    [Fact]
    public async Task Offline_serves_cache_immediately_without_calling_the_network()
    {
        var cached = Records();
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync(RecordsKey, cached);

        var inner = new Mock<IMobileApiClient>(MockBehavior.Strict); // any network call fails the test
        var sut = Sut(inner.Object, cache, online: false);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.True(result.IsSuccess);
        Assert.Same(cached, result.Value);
        inner.Verify(x => x.GetRecordsAsync(It.IsAny<FacilityCode?>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task Offline_with_no_cache_returns_failure_without_calling_the_network()
    {
        var inner = new Mock<IMobileApiClient>(MockBehavior.Strict);
        var sut = Sut(inner.Object, new FakeOfflineReadCache(), online: false);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.False(result.IsSuccess);
        inner.Verify(x => x.GetRecordsAsync(It.IsAny<FacilityCode?>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task Online_but_unreachable_falls_back_to_cache()
    {
        var cached = Records();
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync(RecordsKey, cached);

        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .ThrowsAsync(new HttpRequestException()); // online flag true, but server/tunnel down
        var sut = Sut(inner.Object, cache, online: true);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.True(result.IsSuccess);
        Assert.Same(cached, result.Value);
    }

    [Fact]
    public async Task Non_connectivity_exception_is_not_swallowed()
    {
        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .ThrowsAsync(new InvalidOperationException("a real bug"));
        var sut = Sut(inner.Object, new FakeOfflineReadCache(), online: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetRecordsAsync(null, From, To));
    }

    /// <summary>
    /// A read that outruns its budget answers from the last-known value instead of holding the screen.
    /// </summary>
    /// <remarks>
    /// Reads and writes used to share one ten-second HTTP timeout. That number was right for a read — which has a cache to fall
    /// back on — and wrong for a WRITE, which has none: a collector reported three saves cancelled on a weak signal. The client's
    /// timeout is now the write budget, so reads carry their own short one here, and this is what keeps a slow read as prompt as
    /// it was before.
    /// </remarks>
    [Fact]
    public async Task A_read_that_outruns_its_budget_is_answered_from_cache()
    {
        var cached = Records();
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync(RecordsKey, cached);

        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return Result<IReadOnlyList<MobileCollectorRecordDto>>.Success(Records());
            });

        var sut = new CachingMobileApiClient(
            inner.Object, cache, new FakeConnectivityMonitor(true), TimeSpan.FromMilliseconds(50));

        var result = await sut.GetRecordsAsync(null, From, To);

        // The cached value, not the one still in flight — and returned without waiting the five seconds out.
        Assert.True(result.IsSuccess);
        Assert.Same(cached, result.Value);
    }

    /// <summary>
    /// The read that arrived late is not thrown away: it warms the cache so the NEXT read is fresher.
    /// </summary>
    /// <remarks>
    /// Before, the HTTP client cancelled such a request outright. Now the client's timeout is longer than the read's budget, so a
    /// response that misses the budget by a moment still has value — the collector has already been answered, but the next screen
    /// need not be stale. Asserted on the cache rather than the return value, because the caller is deliberately not made to wait.
    /// </remarks>
    [Fact]
    public async Task A_late_read_still_warms_the_cache()
    {
        var stale = Records();
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync(RecordsKey, stale);

        var late = Records();
        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150));
                return Result<IReadOnlyList<MobileCollectorRecordDto>>.Success(late);
            });

        var sut = new CachingMobileApiClient(
            inner.Object, cache, new FakeConnectivityMonitor(true), TimeSpan.FromMilliseconds(20));

        var served = await sut.GetRecordsAsync(null, From, To);
        Assert.Same(stale, served.Value);

        // Give the abandoned request time to land. Polled rather than slept once, so the test does not depend on a machine's speed.
        for (var i = 0; i < 100; i++)
        {
            if (ReferenceEquals(await cache.GetAsync<IReadOnlyList<MobileCollectorRecordDto>>(RecordsKey), late))
                break;
            await Task.Delay(20);
        }

        Assert.Same(late, await cache.GetAsync<IReadOnlyList<MobileCollectorRecordDto>>(RecordsKey));
    }

    [Fact]
    public async Task Server_failure_is_returned_as_is_and_not_cached()
    {
        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Unauthorized());
        var cache = new FakeOfflineReadCache();
        var sut = Sut(inner.Object, cache, online: true);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.False(cache.Has(RecordsKey));
    }

    [Fact]
    public async Task Successful_write_invalidates_collection_entry_caches_but_preserves_review_menu_and_profile()
    {
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync("menu", "menu-data");
        await cache.SetAsync("profile", "profile-data");
        await cache.SetAsync("monthly|TCC|2026|6", "monthly-data");
        await cache.SetAsync("records|all|2026-06-01|2026-06-30", "records-data");

        var inner = new Mock<IMobileApiClient>();
        var request = new RecordMobileMonthlyCollectionRequest(Guid.NewGuid(), PaymentStatus.Paid, null, "OR1");
        inner.Setup(x => x.RecordMonthlyCollectionAsync(request)).ReturnsAsync(Result<bool>.Success(true));
        var sut = Sut(inner.Object, cache, online: true);

        var result = await sut.RecordMonthlyCollectionAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(cache.Has("monthly|TCC|2026|6"));               // collection-entry view → invalidated
        Assert.True(cache.Has("records|all|2026-06-01|2026-06-30")); // offline review view → preserved (survives for offline)
        Assert.True(cache.Has("menu"));                              // preserved (offline app-open depends on it)
        Assert.True(cache.Has("profile"));                           // preserved
    }

    [Fact]
    public async Task Transient_server_failure_falls_back_to_cache_when_available()
    {
        var cached = Records();
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync(RecordsKey, cached);

        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Failure("bad gateway", 502)); // flaky tunnel
        var sut = Sut(inner.Object, cache, online: true);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.True(result.IsSuccess);   // stale served instead of surfacing the transient error
        Assert.Same(cached, result.Value);
    }

    [Fact]
    public async Task Transient_server_failure_without_cache_returns_the_failure()
    {
        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, From, To))
            .ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Failure("server error", 500));
        var sut = Sut(inner.Object, new FakeOfflineReadCache(), online: true);

        var result = await sut.GetRecordsAsync(null, From, To);

        Assert.False(result.IsSuccess);  // nothing cached → surface the real failure (don't fake success)
        Assert.Equal(ResultStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Failed_write_does_not_invalidate_caches()
    {
        var cache = new FakeOfflineReadCache();
        await cache.SetAsync("monthly|TCC|2026|6", "monthly-data");

        var inner = new Mock<IMobileApiClient>();
        var request = new RecordMobileMonthlyCollectionRequest(Guid.NewGuid(), PaymentStatus.Paid, null, "OR1");
        inner.Setup(x => x.RecordMonthlyCollectionAsync(request)).ReturnsAsync(Result<bool>.Failure("nope", 400));
        var sut = Sut(inner.Object, cache, online: true);

        await sut.RecordMonthlyCollectionAsync(request);

        Assert.True(cache.Has("monthly|TCC|2026|6")); // unchanged on failure
    }
}
