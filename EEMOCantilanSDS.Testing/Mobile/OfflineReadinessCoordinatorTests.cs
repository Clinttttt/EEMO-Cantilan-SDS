using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Mobile.Services;
using Moq;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public sealed class OfflineReadinessCoordinatorTests
{
    private static MobileMenuDto Menu(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Collector", "EMP-1", new DateOnly(2026, 10, 24), []);

    private static CollectorOperationCapabilityDto Operation(string code, bool assigned = true, bool collectible = true) =>
        new(code, code, assigned, CollectorOperationCapabilityStatus.Ready, collectible, []);

    private static async Task CompletedAsync(OfflineReadinessCoordinator coordinator)
    {
        for (var i = 0; i < 100 && coordinator.IsPreparing; i++)
            await Task.Delay(10);
        Assert.False(coordinator.IsPreparing);
    }

    [Fact]
    public async Task Menu_preparation_fetches_only_assigned_collectible_operations_without_navigation()
    {
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetOperationTermsAsync("MARKET_FEES", null))
            .ReturnsAsync(Result<GovernedServiceTermsDto>.Failure("No current terms", 503));
        using var readiness = new OfflineReadinessCoordinator(api.Object, new FakeConnectivityMonitor());
        readiness.Start(Menu(), [Operation("MARKET_FEES"), Operation("LANDING_BERTHING", assigned: false)]);
        await CompletedAsync(readiness);
        Assert.Equal(1, readiness.Total);
        Assert.Equal(1, readiness.Completed);
        Assert.Equal(1, readiness.Failed);
        Assert.Contains("partially", readiness.StatusText);
        api.Verify(x => x.GetOperationTermsAsync("MARKET_FEES", null), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Menu_does_not_wait_for_slow_reads_or_start_a_duplicate_preparation()
    {
        var tcs = new TaskCompletionSource<Result<GovernedServiceTermsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetOperationTermsAsync("MARKET_FEES", null)).Returns(tcs.Task);
        var menu = Menu();
        var operations = new[] { Operation("MARKET_FEES") };
        using var readiness = new OfflineReadinessCoordinator(api.Object, new FakeConnectivityMonitor());
        readiness.Start(menu, operations);
        readiness.Start(menu, operations);
        Assert.True(readiness.IsPreparing);
        Assert.Equal(1, readiness.Total);
        api.Verify(x => x.GetOperationTermsAsync("MARKET_FEES", null), Times.Once);
        tcs.SetResult(Result<GovernedServiceTermsDto>.Failure("Unavailable", 503));
        await CompletedAsync(readiness);
        Assert.Equal(1, readiness.Completed);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Collector_switch_discards_stale_results_from_previous_preparation()
    {
        var previous = new TaskCompletionSource<Result<GovernedServiceTermsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetOperationTermsAsync("MARKET_FEES", null)).Returns(previous.Task);
        using var readiness = new OfflineReadinessCoordinator(api.Object, new FakeConnectivityMonitor());
        readiness.Start(Menu(), [Operation("MARKET_FEES")]);
        readiness.Start(Menu(), []); // authenticated collector changed before the request returned
        previous.SetResult(Result<GovernedServiceTermsDto>.Failure("Old request", 503));
        await Task.Delay(30);
        Assert.Equal(0, readiness.Total);
        Assert.Equal(0, readiness.Failed);
        Assert.Equal(0, readiness.Completed);
        api.Verify(x => x.GetOperationTermsAsync("MARKET_FEES", null), Times.Once);
    }

    [Fact]
    public void Offline_menu_does_not_schedule_network_prefetch()
    {
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        using var readiness = new OfflineReadinessCoordinator(api.Object, new FakeConnectivityMonitor(online: false));
        readiness.Start(Menu(), [Operation("MARKET_FEES")]);
        Assert.False(readiness.IsPreparing);
        Assert.Contains("Offline", readiness.StatusText);
        api.VerifyNoOtherCalls();
    }
}
