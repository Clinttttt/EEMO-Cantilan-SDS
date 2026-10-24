using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Mobile.Services;
using Moq;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public sealed class CachingMobileSessionIsolationTests
{
    [Fact]
    public async Task Previous_collectors_slow_success_cannot_repopulate_new_login_cache()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);
        var response = new TaskCompletionSource<Result<IReadOnlyList<MobileCollectorRecordDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, from, to)).Returns(response.Task);
        var cache = new FakeOfflineReadCache();
        var sut = new CachingMobileApiClient(inner.Object, cache, new FakeConnectivityMonitor());
        var oldRead = sut.GetRecordsAsync(null, from, to);

        await sut.ClearForIdentityChangeAsync();
        response.SetResult(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success([]));

        var completed = await oldRead;
        Assert.False(completed.IsSuccess);
        Assert.False(cache.Has("records|all|2026-06-01|2026-06-30"));
    }

    [Fact]
    public async Task Old_background_cache_warm_cannot_write_after_logout()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);
        var response = new TaskCompletionSource<Result<IReadOnlyList<MobileCollectorRecordDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new Mock<IMobileApiClient>();
        inner.Setup(x => x.GetRecordsAsync(null, from, to)).Returns(response.Task);
        var cache = new FakeOfflineReadCache();
        var sut = new CachingMobileApiClient(inner.Object, cache, new FakeConnectivityMonitor(), TimeSpan.FromMilliseconds(10));
        _ = await sut.GetRecordsAsync(null, from, to); // timed out; GET continues to warm the cache
        await sut.ClearForIdentityChangeAsync();
        response.SetResult(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success([]));

        await Task.Delay(60);
        Assert.False(cache.Has("records|all|2026-06-01|2026-06-30"));
    }
}
