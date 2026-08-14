using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// SettleNpmMonth records a whole NPM month as collected in one action (the formal Pay-bill form),
/// so the office never clicks day-by-day. It must mark every collectable, non-future, not-yet-paid day
/// paid, stamp the receipt (OR) on every settled day (one receipt covers the month), and refuse non-NPM stalls.
/// </summary>
public class SettleNpmMonthCommandHandlerTests
{
    private static Stall NpmStallWithContract(DateOnly effectivity, int years)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        stall.Contracts.Add(Contract.Create(stall.Id, "Vendor", "Vendor", effectivity, years, 900m));
        return stall;
    }

    [Fact]
    public async Task Settle_MarksEveryCollectableDayOfPastMonthPaid_AndStampsOrOnEveryDay()
    {
        var today = PhilippineTime.Today;
        var target = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);   // a fully-past month
        var daysInMonth = DateTime.DaysInMonth(target.Year, target.Month);
        var stall = NpmStallWithContract(target.AddMonths(-3), 3);             // contract spans the month

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        closureRepo.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());
        paymentRepo.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        currentUser.SetupGet(c => c.Username).Returns("admin");                // Role null → not a collector
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        var handler = new SettleNpmMonthCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object,
            closureRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var result = await handler.Handle(
            new SettleNpmMonthCommand(stall.Id, target.Year, target.Month, "OR-777"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Thirty days: the month's rent. A 31-day month is not billed an extra day here — that day stays open for
        // the daily calendar, where the office collects it as revenue beyond the rent.
        var expectedDays = Math.Min(daysInMonth, DomainRules.DailyBilledMonthDays);
        Assert.Equal(expectedDays, captured.Count);
        Assert.Equal(FeeRates.NpmDailyFee * expectedDays, captured.Sum(dc => dc.DailyFee));
        Assert.All(captured, dc => Assert.True(dc.IsPaid));
        Assert.All(captured, dc => Assert.Equal("OR-777", dc.ORNumber));   // one receipt covers every day
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_RejectsNonNpmStall()
    {
        var stall = Stall.Create(Guid.NewGuid(), "101", 2400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC"));

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var handler = new SettleNpmMonthCommandHandler(
            new Mock<IDailyCollectionRepository>().Object, new Mock<IPaymentRepository>().Object, stallRepo.Object,
            new Mock<ICollectorRepository>().Object, currentUser.Object, new Mock<INpmMarketClosureRepository>().Object,
            new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var result = await handler.Handle(new SettleNpmMonthCommand(stall.Id, 2026, 6, null), CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }

    // ── A past month on a stall that has since been re-let ────────────────────────────────────────────
    // The month belongs to the lessee who held the stall then. Resolving "this stall's occupancy" as the most
    // recent one settled none of that month's days and still reported success, so the office was told a payment
    // had been recorded when nothing had.

    private static (Stall Stall, Contract Past, Contract Current) ReLetNpmStall(DateOnly pastMonthStart)
    {
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));

        // The departed lessee held the stall through the target month and was handed over the month after it.
        var handover = pastMonthStart.AddMonths(1);
        var past = Contract.Create(stall.Id, "Departed Lessee", "Departed Lessee", pastMonthStart.AddMonths(-6), 3, 900m);
        past.Terminate("Admin", handover.AddDays(-1));
        var current = Contract.Create(stall.Id, "Sitting Lessee", "Sitting Lessee", handover, 3, 900m);
        stall.Contracts.Add(past);
        stall.Contracts.Add(current);
        return (stall, past, current);
    }

    private static (SettleNpmMonthCommandHandler Handler, List<DailyCollection> Captured, Mock<IUnitOfWork> Uow) BuildHandler(Stall stall)
    {
        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        closureRepo.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());
        paymentRepo.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        currentUser.SetupGet(c => c.Username).Returns("admin");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        var handler = new SettleNpmMonthCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, new Mock<ICollectorRepository>().Object,
            currentUser.Object, closureRepo.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        return (handler, captured, uow);
    }

    [Fact]
    public async Task Settle_PastMonthOnAReLetStall_SettlesTheMonthsOwnOccupancy_WithoutNamingIt()
    {
        var today = PhilippineTime.Today;
        var target = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var daysInMonth = DateTime.DaysInMonth(target.Year, target.Month);
        var (stall, _, _) = ReLetNpmStall(target);
        var (handler, captured, uow) = BuildHandler(stall);

        // No ContractId — the follow-up rows for a period carry none.
        var result = await handler.Handle(
            new SettleNpmMonthCommand(stall.Id, target.Year, target.Month, "OR-1001"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(daysInMonth, captured.Count);        // the departed lessee's whole month, not zero days
        Assert.All(captured, dc => Assert.True(dc.IsPaid));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_AMonthNoOccupancyAnswersFor_IsReportedAsAFailure_NotASilentSuccess()
    {
        var today = PhilippineTime.Today;
        var target = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var (stall, _, _) = ReLetNpmStall(target);
        var (handler, captured, uow) = BuildHandler(stall);

        // A month before the stall was ever let: nothing owes anything for it.
        var before = target.AddMonths(-24);

        var result = await handler.Handle(
            new SettleNpmMonthCommand(stall.Id, before.Year, before.Month, "OR-1002"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(captured);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Settle_ANamedTerm_StillBoundsTheMonthToThatLesseesOwnDays()
    {
        // A handover month settled from the departed lessee's own row stops at their last day — the sitting
        // lessee's days are not paid by that receipt.
        var today = PhilippineTime.Today;
        var target = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));

        var handoverDay = target.AddDays(9);                       // the 10th of the target month
        var past = Contract.Create(stall.Id, "Departed Lessee", "Departed Lessee", target.AddMonths(-6), 3, 900m);
        past.Terminate("Admin", handoverDay.AddDays(-1));
        var current = Contract.Create(stall.Id, "Sitting Lessee", "Sitting Lessee", handoverDay, 3, 900m);
        stall.Contracts.Add(past);
        stall.Contracts.Add(current);

        var (handler, captured, _) = BuildHandler(stall);

        var result = await handler.Handle(
            new SettleNpmMonthCommand(stall.Id, target.Year, target.Month, "OR-1003", ContractId: past.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(9, captured.Count);                            // the 1st to the 9th only
        Assert.All(captured, dc => Assert.True(dc.CollectionDate < handoverDay));
    }
}
