using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
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

    private static MobileMenuDto EighteenStepMenu() =>
        Menu() with { Facilities =
        [
            Facility(FacilityCode.NPM, BillingArchetype.DailyStall),
            Facility(FacilityCode.TCC, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.NCC, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.BBQ, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.ICE, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.Custom1, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.Custom2, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.Custom3, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.Custom4, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.Custom5, BillingArchetype.MonthlyRental),
            Facility(FacilityCode.SLH, BillingArchetype.PerHead),
            Facility(FacilityCode.TPM, BillingArchetype.WeeklyMarket)
        ] };

    private static MobileFacilityMenuItemDto Facility(FacilityCode code, BillingArchetype archetype) =>
        new(code, code.ToString(), "", true, true, archetype);

    private static Mock<IMobileApiClient> EighteenStepApi(Func<bool> failTwo)
    {
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetNpmCollectionAsync(2026, 10))
            .ReturnsAsync(Result<MobileNpmCollectionDto>.Success(null!));
        api.Setup(x => x.GetNpmArrearsAsync(2026, 10))
            .ReturnsAsync(Result<MobileNpmArrearsDto>.Success(null!));
        api.Setup(x => x.GetNpmUtilityAsync(2026, 10))
            .ReturnsAsync(Result<MobileNpmUtilityDto>.Success(null!));
        api.Setup(x => x.GetMonthlyCollectionAsync(It.IsAny<FacilityCode>(), 2026, 10))
            .ReturnsAsync(Result<MobileMonthlyCollectionDto>.Success(null!));
        api.Setup(x => x.GetSlaughterTermsAsync(new DateOnly(2026, 10, 24)))
            .ReturnsAsync(Result<SlaughterTermsDto>.Success(null!));
        api.Setup(x => x.GetSlaughterCollectionAsync(2026, 10, 24))
            .ReturnsAsync(Result<MobileSlaughterCollectionDto>.Success(null!));
        api.Setup(x => x.GetTpmCollectionAsync())
            .ReturnsAsync(Result<MobileTpmCollectionDto>.Success(null!));
        api.Setup(x => x.GetWcfSourcesAsync(2026, 10))
            .ReturnsAsync(Result<IReadOnlyList<WcfMobileSourceDto>>.Success([]));
        api.Setup(x => x.GetWcfObligationsAsync(2026, 10))
            .ReturnsAsync(() => failTwo()
                ? Result<IReadOnlyList<WcfObligationQuoteDto>>.Failure("PAYOR-DETAILS-MUST-NOT-BE-STORED", 503)
                : Result<IReadOnlyList<WcfObligationQuoteDto>>.Success([]));
        api.Setup(x => x.GetMobileEcfSourcesAsync())
            .ReturnsAsync(() => failTwo()
                ? Result<IReadOnlyList<EcfObligationQuoteDto>>.Failure("SENSITIVE-SERVER-ERROR", 409)
                : Result<IReadOnlyList<EcfObligationQuoteDto>>.Success([]));
        return api;
    }

    private static CollectorOperationCapabilityDto[] EighteenStepOperations() =>
        [Operation("WCF"), Operation("ECF")];

    [Fact]
    public async Task All_eighteen_assigned_steps_succeed_without_failure_diagnostics()
    {
        var api = EighteenStepApi(() => false);
        using var readiness = new OfflineReadinessCoordinator(api.Object, new FakeConnectivityMonitor());
        readiness.Start(EighteenStepMenu(), EighteenStepOperations());
        await CompletedAsync(readiness);
        Assert.Equal(18, readiness.Total);
        Assert.Equal(18, readiness.Completed);
        Assert.Equal(0, readiness.Failed);
        Assert.Empty(readiness.FailedSteps);
        api.Verify(x => x.GetMonthlyCollectionAsync(It.IsAny<FacilityCode>(), 2026, 10), Times.Exactly(9));
    }

    [Fact]
    public async Task Two_failed_steps_are_identifiable_without_source_or_error_data_and_recover_on_reconnect()
    {
        var fail = true;
        var api = EighteenStepApi(() => fail);
        var connectivity = new FakeConnectivityMonitor();
        using var readiness = new OfflineReadinessCoordinator(api.Object, connectivity);
        readiness.Start(EighteenStepMenu(), EighteenStepOperations());
        await CompletedAsync(readiness);
        Assert.Equal(18, readiness.Total);
        Assert.Equal(18, readiness.Completed);
        Assert.Equal(2, readiness.Failed);
        Assert.Equal(new[] { "ecf-sources", "wcf-obligations" },
            readiness.FailedSteps.Select(x => x.StepId).OrderBy(x => x).ToArray());
        Assert.All(readiness.FailedSteps, x => Assert.Equal(OfflinePreloadFailureKind.UnsuccessfulResponse, x.Kind));
        Assert.DoesNotContain("SENSITIVE", string.Join(";", readiness.FailedSteps));
        Assert.DoesNotContain("PAYOR", string.Join(";", readiness.FailedSteps));
        Assert.Contains("16/18", readiness.StatusText);

        fail = false;
        connectivity.RaiseRestored();
        await CompletedAsync(readiness);
        Assert.Equal(18, readiness.Completed);
        Assert.Equal(0, readiness.Failed);
        Assert.Empty(readiness.FailedSteps);
    }

    [Theory]
    [InlineData(true, OfflinePreloadFailureKind.Network)]
    [InlineData(false, OfflinePreloadFailureKind.Timeout)]
    public async Task Transport_and_timeout_failures_record_only_a_safe_reason_class(
        bool networkFailure, OfflinePreloadFailureKind expected)
    {
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetOperationTermsAsync("MARKET_FEES", null))
            .Returns(Task.FromException<Result<GovernedServiceTermsDto>>(networkFailure
                ? new HttpRequestException("private payor content") : new TaskCanceledException("secret operation detail")));
        using var readiness = new OfflineReadinessCoordinator(api.Object, new FakeConnectivityMonitor());
        readiness.Start(Menu(), [Operation("MARKET_FEES")]);
        await CompletedAsync(readiness);
        var failure = Assert.Single(readiness.FailedSteps);
        Assert.Equal("terms-MARKET_FEES", failure.StepId);
        Assert.Equal(expected, failure.Kind);
        Assert.DoesNotContain("private", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stop_and_dispose_discard_in_flight_private_diagnostics()
    {
        var pending = new TaskCompletionSource<Result<GovernedServiceTermsDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new Mock<IMobileApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetOperationTermsAsync("MARKET_FEES", null)).Returns(pending.Task);
        var connectivity = new FakeConnectivityMonitor();
        var readiness = new OfflineReadinessCoordinator(api.Object, connectivity);
        readiness.Start(Menu(), [Operation("MARKET_FEES")]);
        Assert.True(readiness.IsPreparing);
        readiness.Dispose();
        pending.SetResult(Result<GovernedServiceTermsDto>.Failure("DO NOT STORE", 500));
        await Task.Delay(30);
        Assert.Equal(0, readiness.Total);
        Assert.Equal(0, readiness.Completed);
        Assert.Equal(0, readiness.Failed);
        Assert.Empty(readiness.FailedSteps);
        connectivity.RaiseRestored();
        Assert.Empty(readiness.FailedSteps);
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
