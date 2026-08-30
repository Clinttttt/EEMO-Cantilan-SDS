using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        // Past month (March 2026): ~31 payable days, but the captured amount only covers 3 × ₱30 = ₱90.
        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 3, collectorId: null, recordedBy: "Online", CancellationToken.None, maxAmount: 90m);

        Assert.Equal(3, settled.Count);                       // capped to what was paid for
        Assert.All(settled, dc => Assert.True(dc.IsPaid));
    }

    /// <summary>
    /// What a custom-section stall's month owes once its daily fee is a rounded whole peso.
    /// </summary>
    /// <remarks>
    /// Written during an audit of the form that works a daily fee out from a monthly rent, after reasoning about it twice
    /// and being wrong twice. The measurement is the point.
    ///
    /// <para>
    /// A stall in one of the office's own sections is let at its OWN daily rate, and
    /// <c>Stall.ResolveMonthlyRent</c> makes its month thirty of those - the office's stated market month does not apply to
    /// a section it does not price. So a clerk who types ₱800 a month gets a derived ₱27 a day, and the month this stall
    /// owes is ₱810, not the ₱800 typed above it. The stall's own MonthlyRate field records what the CONTRACT says; it is
    /// not what a daily-billed month bills.
    /// </para>
    /// <para>
    /// The divergence is not created by rounding - at ₱26.67 the month owed ₱800.10 - but rounding widens it from ten
    /// centavos to ten pesos. Recorded rather than "fixed", because the alternative is centavos in a fee a collector takes
    /// in cash. See OUTSTANDING_WORK.md.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACustomSectionStallsMonthIsThirtyOfItsOwnRoundedDailyFee()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        // A stall in the office's own section, let at the ₱27 a day the form derives from ₱800 a month.
        var stall = Stall.Create(
            npm.Id, "12", monthlyRate: 800m, fees: ApplicableFees.DailyRental,
            section: null, dailyRate: 27m, customSectionName: "Sari-sari Area");
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 800m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());

        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        // March 2026 is a fully past month, so every day of it is payable.
        var month = await svc.ComputePayableAsync(stall, 2026, 3, CancellationToken.None);

        // Thirty installments of ₱27, and no thirty-first: the month is let for thirty, whatever the calendar gave it.
        Assert.Equal(30, month.Days);
        Assert.Equal(810m, month.Amount);
        Assert.Equal(0m, month.Adjustment);
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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        // Settling "the month" settles what the month owes. March has 31 days, but the space is let for ₱900 a
        // month, so thirty days are marked and the thirty-first stays open for the daily calendar — the office can
        // still collect it there, as revenue beyond the rent.
        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 3, collectorId: null, recordedBy: "Admin", CancellationToken.None);

        Assert.Equal(DomainRules.DailyBilledMonthDays, settled.Count);
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, settled.Sum(dc => dc.DailyFee));
    }

    [Fact]
    public async Task ComputePayableForDays_QuotesTheEarliestDays_AndSettlementSettlesExactlyThose()
    {
        // Paying part of a month is only honest if the money quoted reaches exactly as far down the month as the money
        // charged: quote three days, settle three days, and the three the office would have collected first.
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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var quote = await svc.ComputePayableForDaysAsync(stall, 2026, 3, 3, CancellationToken.None);

        Assert.Equal(3, quote.Days);
        Assert.Equal(FeeRates.NpmDailyFee * 3, quote.Amount);
        Assert.Equal(0m, quote.Adjustment);                    // part of a month closes nothing

        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 3, collectorId: null, recordedBy: "Online", CancellationToken.None, maxAmount: quote.Amount);

        Assert.Equal(3, settled.Count);
        Assert.Equal(new[] { 1, 2, 3 }, settled.Select(dc => dc.CollectionDate.Day).ToArray());
    }

    [Fact]
    public async Task ComputePayableForDays_AskingForTheWholeMonth_IsTheWholeMonthsOwnQuote()
    {
        // "All the days I owe" must be the same figure as the month quote, by the same rule, or the two screens that show
        // them would disagree.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var month = await svc.ComputePayableAsync(stall, 2026, 3, CancellationToken.None);
        var asked = await svc.ComputePayableForDaysAsync(stall, 2026, 3, 31, CancellationToken.None);

        Assert.Equal(month.Days, asked.Days);
        Assert.Equal(month.Amount, asked.Amount);
        Assert.Equal(month.Adjustment, asked.Adjustment);
    }

    [Fact]
    public async Task ComputePayableForDays_ForAClosedShortMonth_RefusesPartOfIt()
    {
        // February's twenty-eight days at ₱30 fall ₱60 short of the ₱900 rent, and that difference rides on the LAST
        // installment settled. Part-settling such a month would take money for days settlement would then decline to
        // mark, so nothing is quoted for part of it and the caller refuses.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var quote = await svc.ComputePayableForDaysAsync(stall, 2026, 2, 3, CancellationToken.None);

        Assert.Equal(0, quote.Days);
        Assert.Equal(0m, quote.Amount);
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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var payable = await svc.ComputePayableAsync(stall, 2026, 3, CancellationToken.None);

        Assert.Equal(0, payable.Days);
        Assert.Equal(0m, payable.Amount);
    }

    [Fact]
    public async Task ComputePayable_ForAClosedShortMonth_IncludesTheMonthEndAdjustment()
    {
        // February's twenty-eight installments come to ₱840 and its rent is ₱900, so once the month has closed the
        // ₱60 difference is a collectible month-end adjustment: the office and the payor are asked the month's rent.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var payable = await svc.ComputePayableAsync(stall, 2026, 2, CancellationToken.None);

        Assert.Equal(28, payable.Days);
        Assert.Equal(60m, payable.Adjustment);
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, payable.Amount);   // ₱900
    }

    [Fact]
    public async Task SettleUnpaidDays_ForAClosedShortMonth_ReachesTheRent_WithTheAdjustmentOnTheLastInstallment()
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        daily.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 2, collectorId: null, recordedBy: "Admin", CancellationToken.None);

        Assert.Equal(28, settled.Count);
        // The month's ledger reaches its rent exactly: twenty-seven ordinary installments and a last one carrying
        // the month-end adjustment, so nothing is left that no day could ever clear.
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, settled.Sum(dc => dc.DailyFee));
        Assert.Equal(60m, settled[^1].MonthEndAdjustment);
        Assert.Equal(FeeRates.NpmDailyFee + 60m, settled[^1].DailyFee);
        Assert.All(settled.Take(27), dc => Assert.Null(dc.MonthEndAdjustment));
    }

    [Fact]
    public async Task ComputePayable_DoesNotAskForAnAdjustment_BeforeTheMonthsDueDate()
    {
        // The month in progress has not fallen due, so its shortfall is not yet owed: only the elapsed installments
        // are quoted, and no adjustment is added until the month closes.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        var today = PhilippineTime.Today;
        var payable = await svc.ComputePayableAsync(stall, today.Year, today.Month, CancellationToken.None);

        Assert.Equal(0m, payable.Adjustment);
        Assert.Equal(FeeRates.NpmDailyFee * payable.Days, payable.Amount);   // installments only
    }

    [Fact]
    public async Task AClosedShortMonth_EveryDayAlreadyCollected_StillTakesItsAdjustment()
    {
        // The compliant payor: every day of February collected at the stall, ₱840 in, ₱900 owed. No uncollected day
        // remains to carry the ₱60, so it lands on the last installment taken — otherwise this payor would read as
        // ₱60 in arrears for ever, which is precisely the debt the adjustment exists to prevent.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var collected = new List<DailyCollection>();
        for (var day = 1; day <= 28; day++)
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 2, day));
            dc.MarkPaid(string.Empty, collectorId: null);
            collected.Add(dc);
        }

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collected);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        // The month is still quoted its shortfall, with no installments left to pay …
        var payable = await svc.ComputePayableAsync(stall, 2026, 2, CancellationToken.None);
        Assert.Equal(0, payable.Days);
        Assert.Equal(60m, payable.Adjustment);
        Assert.Equal(60m, payable.Amount);

        // … and settling puts it on the last day collected, bringing the month to its rent exactly.
        var settled = await svc.SettleUnpaidDaysAsync(
            stall, 2026, 2, collectorId: null, recordedBy: "Admin", CancellationToken.None);

        var carrier = Assert.Single(settled);
        Assert.Equal(new DateOnly(2026, 2, 28), carrier.CollectionDate);
        Assert.Equal(60m, carrier.MonthEndAdjustment);
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, collected.Sum(dc => dc.DailyFee));
    }

    [Fact]
    public async Task TheAdjustmentIsTakenOnce_HoweverOftenSettlementRuns()
    {
        // A retried request, or a staff settlement after an online one, must not charge the month twice.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Ramil", "Ramil", new DateOnly(2020, 1, 1), 20, 900m));

        var collected = new List<DailyCollection>();
        for (var day = 1; day <= 28; day++)
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 2, day));
            dc.MarkPaid(string.Empty, collectorId: null);
            collected.Add(dc);
        }

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collected);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

        await svc.SettleUnpaidDaysAsync(stall, 2026, 2, null, "Admin", CancellationToken.None);
        await svc.SettleUnpaidDaysAsync(stall, 2026, 2, null, "Admin", CancellationToken.None);

        Assert.Equal(60m, collected[^1].MonthEndAdjustment);
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, collected.Sum(dc => dc.DailyFee));
    }

    [Fact]
    public void UnpayingAnAdjustedDay_TakesTheAdjustmentBackWithIt()
    {
        // The adjustment is money the month was short, carried on an installment. A day that is no longer a
        // collection carries nothing — leaving the inflated fee would count money the office never received.
        var dc = DailyCollection.Create(Guid.NewGuid(), new DateOnly(2026, 2, 28));
        dc.MarkPaid(string.Empty, collectorId: null);
        dc.AddMonthEndAdjustment(60m, "Admin");
        Assert.Equal(FeeRates.NpmDailyFee + 60m, dc.DailyFee);

        dc.MarkUnpaid("Admin");

        Assert.Null(dc.MonthEndAdjustment);
        Assert.Equal(FeeRates.NpmDailyFee, dc.DailyFee);

        // …and an excused day likewise.
        dc.MarkPaid(string.Empty, collectorId: null);
        dc.AddMonthEndAdjustment(60m, "Admin");
        dc.MarkAbsent("Admin");
        Assert.Null(dc.MonthEndAdjustment);
        Assert.Equal(FeeRates.NpmDailyFee, dc.DailyFee);
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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

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

        var svc = new NpmMonthSettlementService(daily.Object, closures.Object, CacheTestDoubles.FeeRateResolver, new FixedClock(DateTime.UtcNow));

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
