using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Constants;
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
    public async Task SettleUnpaidDays_WithoutACapturedAmount_SettlesTheMonthsRent()
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

        // Settling "the month" settles what the month owes. March has 31 days, but the space is let for ₱900 a
        // month, so thirty days are marked and the thirty-first stays open for the daily calendar — the office can
        // still collect it there, as revenue beyond the rent.
        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 3, collectorId: null, recordedBy: "Admin", CancellationToken.None);

        Assert.Equal(DomainRules.DailyBilledMonthDays, settled.Count);
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, settled.Sum(dc => dc.DailyFee));
    }

    [Fact]
    public async Task ComputePayable_ForA31DayMonth_QuotesTheRent_NotAnExtraDay()
    {
        // The payor is shown a balance of ₱900 for the month; the checkout must ask for ₱900, and the day count
        // beside it must be the days that amount covers — the quote and the settlement can never disagree.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        var payable = await svc.ComputePayableAsync(stall, 2026, 3, CancellationToken.None);

        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, payable.Amount);
        Assert.Equal(DomainRules.DailyBilledMonthDays, payable.Days);
    }

    [Fact]
    public async Task ComputePayable_WhenTheRentIsAlreadyIn_AsksForNothing()
    {
        // Thirty days collected settles a 31-day month, so the checkout must not offer the thirty-first as a debt.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var collected = new List<DailyCollection>();
        for (var day = 1; day <= 30; day++)
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 3, day));
            dc.MarkPaid(string.Empty, collectorId: null);
            collected.Add(dc);
        }

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collected);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        var payable = await svc.ComputePayableAsync(stall, 2026, 3, CancellationToken.None);

        Assert.Equal(0, payable.Days);
        Assert.Equal(0m, payable.Amount);
    }

    [Fact]
    public async Task QuoteFishDay_PricesBasePlusDeclaredKilos()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        stall.Contracts.Add(Contract.Create(stall.Id, "Lito", "Lito", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndDateAsync(stall.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);   // that day is uncollected
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        // Past, in-term, uncollected fish day + 54 kg → ₱30 base + 54 × ₱1 = ₱84 (ordinance fallback rates).
        var quote = await svc.QuoteFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, CancellationToken.None);

        Assert.True(quote.IsPayable);
        Assert.Equal(30m, quote.BaseFee);
        Assert.Equal(1m, quote.FishRatePerKilo);
        Assert.Equal(84m, quote.Amount);
    }

    [Fact]
    public async Task QuoteFishDay_AlreadyCollectedDay_IsNotPayable()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        stall.Contracts.Add(Contract.Create(stall.Id, "Lito", "Lito", new DateOnly(2020, 1, 1), 20, 900m));

        var collected = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 15));
        collected.MarkPaid("OR-1", collectorId: Guid.NewGuid(), fishKilos: 40m);

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndDateAsync(stall.Id, new DateOnly(2026, 6, 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collected);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        var quote = await svc.QuoteFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, CancellationToken.None);

        Assert.False(quote.IsPayable);   // already collected in person → not payable online
    }

    [Fact]
    public async Task SettleFishDay_MarksThatDayPaid_WithDeclaredKilos_BlankOr_NoCollector()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        stall.Contracts.Add(Contract.Create(stall.Id, "Lito", "Lito", new DateOnly(2020, 1, 1), 20, 900m));

        DailyCollection? added = null;
        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndDateAsync(stall.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        daily.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => added = dc)
            .Returns(Task.CompletedTask);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver);

        var dc = await svc.SettleFishDayAsync(stall, new DateOnly(2026, 6, 15), 54m, "Online", CancellationToken.None);

        Assert.NotNull(added);
        Assert.Same(added, dc);
        Assert.True(dc!.IsPaid);
        Assert.Equal(54m, dc.FishKilos);
        Assert.Equal(30m, dc.DailyFee);              // as-of base stamped
        Assert.Equal(string.Empty, dc.ORNumber);     // blank OR — staff encode later
        Assert.Null(dc.CollectorId);                 // online — no collector
        Assert.Equal(new DateOnly(2026, 6, 15), dc.CollectionDate);
    }
}
