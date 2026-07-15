using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Testing.Support;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The shared NPM month-settlement service caps an online settlement to the captured amount, so a
/// checkout that crossed midnight (exposing an extra unpaid day) can never settle more days than were
/// paid for. Fee falls back to the ₱30 ordinance constant (empty rate table).
/// </summary>
public class NpmMonthSettlementServiceTests
{
    [Fact]
    public async Task SettleUnpaidDays_CapsToCapturedAmount()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        // Contract covers a fully-past month → every day is elapsed + payable (no existing rows, no closures).
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        daily.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        // Past month (March 2026): ~31 payable days, but the captured amount only covers 3 × ₱30 = ₱90.
        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 3, collectorId: null, recordedBy: "Online", CancellationToken.None, maxAmount: 90m);

        Assert.Equal(3, settled.Count);                       // capped to what was paid for
        Assert.All(settled, dc => Assert.True(dc.IsPaid));
    }

    [Fact]
    public async Task SettleUnpaidDays_NoCap_SettlesAllPayableDays()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        daily.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        // Without a cap (the staff path), every payable day of March is settled.
        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 3, collectorId: null, recordedBy: "Admin", CancellationToken.None);

        Assert.Equal(31, settled.Count);
    }
}
