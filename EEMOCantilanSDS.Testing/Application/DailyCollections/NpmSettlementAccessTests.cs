using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.DailyCollections;

/// <summary>
/// Who may record that the market's money was received.
///
/// <para>
/// Settling is how the office states that a collection happened, so the rule about who may do it is an authorisation rule, not a
/// convenience. A collector may settle only where they are assigned; an administrator may settle. It applies identically whether
/// a chosen set of DAYS is settled or a whole MONTH, because settling a month is only that done repeatedly.
/// </para>
///
/// <para>
/// The rule was written out twice, once in each handler, and NEITHER copy was tested: every existing test for these handlers runs
/// as an administrator, so the collector branch never executed. Both handlers now call one guard, and these cover it from both
/// entry points — a rule kept in one place still needs proving at each door that uses it.
/// </para>
/// </summary>
public class NpmSettlementAccessTests
{
    private static Stall NpmStall()
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 0m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);

        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall, facility);
        stall.Contracts.Add(Contract.Create(
            stall.Id, "Kim Chui", "Kim Chui", PhilippineTime.Today.AddYears(-1), 3, 900m));

        return stall;
    }

    /// <summary>A collector, optionally assigned to the market.</summary>
    private static CollectorUser Collector(bool assignedToMarket)
    {
        var collector = CollectorUser.Create(
            "Maria", "EEMO-2026-009", "maria", "maria@eemo.gov.ph", "0917", TestPasswords.Hash("Secret123!"));

        if (assignedToMarket)
            collector.FacilityAssignments.Add(
                CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), FacilityCode.NPM));

        return collector;
    }

    private sealed record Doubles(
        Mock<IStallRepository> Stalls,
        Mock<ICollectorRepository> Collectors,
        Mock<ICurrentUserService> CurrentUser,
        Mock<IDailyCollectionRepository> Daily,
        Mock<IPaymentRepository> Payments,
        Mock<INpmMarketClosureRepository> Closures,
        Mock<IUnitOfWork> Uow,
        Stall Stall);

    private static Doubles Setup(CollectorUser? actingCollector, string? role, Guid? collectorId)
    {
        var stall = NpmStall();

        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(actingCollector);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("maria");

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        return new Doubles(stalls, collectors, currentUser, daily, payments, closures, new Mock<IUnitOfWork>(), stall);
    }

    private static SettleNpmDaysCommandHandler DaysHandler(Doubles d) => new(
        d.Daily.Object, d.Payments.Object, d.Stalls.Object, d.Collectors.Object, d.CurrentUser.Object,
        d.Closures.Object, d.Uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver,
        CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

    private static SettleNpmMonthCommandHandler MonthHandler(Doubles d) => new(
        d.Daily.Object, d.Payments.Object, d.Stalls.Object, d.Collectors.Object, d.CurrentUser.Object,
        d.Closures.Object, d.Uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver,
        CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

    private static DateOnly LastMonthFirstDay => new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1).AddMonths(-1);

    [Fact]
    public async Task ACollectorNotAssignedToTheMarketCannotSettleDays()
    {
        var d = Setup(Collector(assignedToMarket: false), "Collector", Guid.NewGuid());

        var result = await DaysHandler(d).Handle(
            new SettleNpmDaysCommand(d.Stall.Id, new[] { LastMonthFirstDay }, "OR-1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
        d.Daily.Verify(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ACollectorNotAssignedToTheMarketCannotSettleAMonth()
    {
        var d = Setup(Collector(assignedToMarket: false), "Collector", Guid.NewGuid());

        var result = await MonthHandler(d).Handle(
            new SettleNpmMonthCommand(d.Stall.Id, LastMonthFirstDay.Year, LastMonthFirstDay.Month, "OR-1"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
        d.Daily.Verify(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ACollectorSessionWithNoCollectorIdIsRefused()
    {
        // Refused rather than trusted: a session claiming the collector role that carries no id is not a collector we can check.
        var d = Setup(Collector(assignedToMarket: true), "Collector", collectorId: null);

        var result = await DaysHandler(d).Handle(
            new SettleNpmDaysCommand(d.Stall.Id, new[] { LastMonthFirstDay }, "OR-1"), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task ACollectorWhoseAccountIsGoneIsRefused()
    {
        var d = Setup(actingCollector: null, "Collector", Guid.NewGuid());

        var result = await DaysHandler(d).Handle(
            new SettleNpmDaysCommand(d.Stall.Id, new[] { LastMonthFirstDay }, "OR-1"), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task ACollectorAssignedToTheMarketMaySettleDays()
    {
        // The other direction, so the guard cannot pass these tests by refusing everyone.
        var d = Setup(Collector(assignedToMarket: true), "Collector", Guid.NewGuid());

        var result = await DaysHandler(d).Handle(
            new SettleNpmDaysCommand(d.Stall.Id, new[] { LastMonthFirstDay }, "OR-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        d.Daily.Verify(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnAdministratorMaySettleWithoutAnyFacilityAssignment()
    {
        // Role null is how the existing tests represent an administrator; the guard must not ask them for an assignment.
        var d = Setup(actingCollector: null, role: null, collectorId: null);

        var result = await DaysHandler(d).Handle(
            new SettleNpmDaysCommand(d.Stall.Id, new[] { LastMonthFirstDay }, "OR-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        d.Collectors.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
