using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// SettleNpmMonth records a whole NPM month as collected in one action (the formal Pay-bill form),
/// so the office never clicks day-by-day. It must mark every collectable, non-future, not-yet-paid day
/// paid, stamp the receipt (OR) on the month's last day, and refuse non-NPM stalls.
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
    public async Task Settle_MarksEveryCollectableDayOfPastMonthPaid_AndStampsOrOnLastDay()
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
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        currentUser.SetupGet(c => c.Username).Returns("admin");                // Role null → not a collector
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        var handler = new SettleNpmMonthCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object,
            closureRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new SettleNpmMonthCommand(stall.Id, target.Year, target.Month, "OR-777"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(daysInMonth, captured.Count);                 // every collectable day recorded
        Assert.All(captured, dc => Assert.True(dc.IsPaid));
        Assert.Single(captured, dc => dc.ORNumber == "OR-777");    // one receipt, stamped on the last day
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
            new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(new SettleNpmMonthCommand(stall.Id, 2026, 6, null), CancellationToken.None);
        Assert.Equal(400, result.StatusCode);
    }
}
