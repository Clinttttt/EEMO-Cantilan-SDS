using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// SettleNpmDays records specific NPM days of one stall as paid in one transaction and stamps ONE
/// Official Receipt across every settled day (a single physical receipt covering the selected days).
/// </summary>
public class SettleNpmDaysCommandHandlerTests
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
    public async Task Settle_MarksSelectedDaysPaid_AndStampsOneOrOnAll()
    {
        var today = PhilippineTime.Today;
        var d1 = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);   // a fully-past month, day 1
        var d2 = d1.AddDays(1);
        var stall = NpmStallWithContract(d1.AddMonths(-3), 3);

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
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        var handler = new SettleNpmDaysCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object,
            closureRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, new[] { d1, d2 }, "OR-DAYS"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, captured.Count);
        Assert.All(captured, dc => Assert.True(dc.IsPaid));
        Assert.All(captured, dc => Assert.Equal("OR-DAYS", dc.ORNumber));   // one receipt covers both days
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

        var handler = new SettleNpmDaysCommandHandler(
            new Mock<IDailyCollectionRepository>().Object, new Mock<IPaymentRepository>().Object, stallRepo.Object,
            new Mock<ICollectorRepository>().Object, currentUser.Object, new Mock<INpmMarketClosureRepository>().Object,
            new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, new[] { new DateOnly(2026, 6, 1) }, null), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }
}
