using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The stall profile's 12-month summary must reconcile with the reports: NPM totals fold daily
/// collections against a contract-aware ₱30/day obligation; pre-contract months are excluded; other
/// facilities use the monthly rent. Counts only fully-covered months as "paid".
/// </summary>
public class StallLedgerSummaryTests : RepositoryTestBase
{
    [Fact]
    public async Task Summary_Npm_FoldsDailyCollectionsAndExcludesPreContractMonths()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        // Contract effective two whole months ago → exactly 3 collectable months in the window.
        var contract = Contract.Create(stall.Id, "Lorna Buenades", "Lorna Buenades", monthStart.AddMonths(-2), 3, 900m);
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", TestPasswords.Hash("pw"));

        // Two paid days this month only (₱60), mirroring the reported scenario.
        var days = new[] { monthStart, monthStart.AddDays(1) }.Where(d => d <= today).ToArray();
        var collections = days.Select(d =>
        {
            var dc = DailyCollection.Create(stall.Id, d);
            dc.MarkPaid($"OR-{d:yyyyMMdd}", collector.Id);
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, contract, collector);
        context.AddRange(collections);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // Collected = exactly the daily fees paid (₱30 × paid days) — not MonthlyRate × count.
        Assert.Equal(days.Length * FeeRates.NpmDailyFee, summary.TotalCollected);
        // 3 collectable months, none fully covered (current is partial, prior two unpaid).
        Assert.Equal(0, summary.MonthsPaid);
        Assert.Equal(3, summary.MonthsUnpaid);
        Assert.True(summary.TotalOutstanding > 0m);

        // The month in progress is billed for the days that have been EARNED, not the whole month. NPM rent accrues
        // per market day, so on the fourth of the month four days are owed, not thirty-one. Billing the month whole
        // made the profile read ₱1,740 for stall 23 beside a grid counting 33 elapsed days; the truthful figure is
        // the two whole months behind plus the days so far this month, less the ₱60 collected.
        var elapsedThisMonth = today.Day;
        // A daily-collected month is LET for a rent, so the days elapsed are what is owed UNTIL they reach it: a 31-day
        // month owes ₱900 and not ₱930, which is the rule DomainRules.DailyBilledMonthCoverage exists to state. Capped
        // here for the same reason - without it this test asserted ₱930 on the 31st of a 31-day month and failed on those
        // days only, which is how it was found.
        var thisMonthOwed = Math.Min(elapsedThisMonth * FeeRates.NpmDailyFee, 900m);
        var expected = (2 * 900m) + thisMonthOwed - summary.TotalCollected;
        Assert.Equal(expected, summary.TotalOutstanding);
    }

    [Fact]
    public async Task Summary_Npm_CurrentMonth_IsNotBilledBeyondToday()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Let from the first of this month and nothing collected: only the elapsed days are owed.
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "23", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Vincent Doloriel", "Vincent Doloriel", monthStart, 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var summary = await new PaymentRepository(context).GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // Capped at the month's rent: the days elapsed are owed until they reach it, and a 31-day month owes ₱900.
        Assert.Equal(Math.Min(today.Day * FeeRates.NpmDailyFee, 900m), summary.TotalOutstanding);
        Assert.True(summary.TotalOutstanding <= 900m, "a month in progress cannot owe more than a whole month's rent");
    }

    [Fact]
    public async Task Summary_NonNpm_PaidMonth_HasNoOutstanding()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", monthStart, 3, 1000m); // starts this month → 1 collectable month
        var payment = PaymentRecord.Create(stall.Id, today.Year, today.Month, 1000m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(1, summary.MonthsPaid);
        Assert.Equal(0, summary.MonthsUnpaid);
        Assert.Equal(1000m, summary.TotalCollected);
        Assert.Equal(0m, summary.TotalOutstanding);
    }

    [Fact]
    public async Task Summary_NonNpm_UnpaidMonth_OwesFullRent()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "102", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", monthStart, 3, 1000m); // 1 collectable month, no payment

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(0, summary.MonthsPaid);
        Assert.Equal(1, summary.MonthsUnpaid);
        Assert.Equal(0m, summary.TotalCollected);
        Assert.Equal(1000m, summary.TotalOutstanding);
    }

    [Fact]
    public async Task Summary_Npm_MonthlyPartialRecord_IsIgnoredInFavorOfDailyCollections()
    {
        // Same reported bug as the history modal: the flat ₱500 monthly partial must not win over
        // the daily collections — the summary must reconcile with the history grid (daily-truth).
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", monthStart.AddMonths(-2), 3, 900m);
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", TestPasswords.Hash("pw"));

        var d1 = DailyCollection.Create(stall.Id, monthStart); d1.MarkPaid("OR-D1", collector.Id);
        var d2 = DailyCollection.Create(stall.Id, monthStart.AddDays(1)); d2.MarkPaid("OR-D2", collector.Id);

        var monthly = PaymentRecord.Create(stall.Id, today.Year, today.Month, 900m);
        monthly.UpdateStatus(PaymentStatus.Partial, 500m);
        monthly.SetOrNumber("MONTHLY-OR-500");

        context.AddRange(facility, stall, contract, collector);
        context.AddRange(d1, d2, monthly);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        Assert.Equal(2 * FeeRates.NpmDailyFee, summary.TotalCollected);  // ₱60 daily-truth, NOT ₱500
        Assert.Equal(0, summary.MonthsPaid);                             // current month only partially covered
        Assert.Equal(3, summary.MonthsUnpaid);                           // 3 collectable months, none fully paid
        Assert.True(summary.TotalOutstanding > 0m);
    }

    [Fact]
    public async Task Summary_Npm_MarketClosedDay_IsExcused_NotOwed()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Lorna", "Lorna", monthStart, 3, 900m); // starts this month → 1 collectable month
        var closure = NpmMarketClosure.Create(monthStart.AddDays(1), MarketClosureReason.Holiday, "Market closed", "Head");
        context.AddRange(facility, stall, contract, closure);

        // Pay every day this month EXCEPT the market-closed day (day 2).
        for (var day = 1; day <= daysInMonth; day++)
        {
            if (day == 2) continue;
            var dc = DailyCollection.Create(stall.Id, new DateOnly(today.Year, today.Month, day));
            dc.MarkPaid(string.Empty, collectorId: null);
            context.Add(dc);
        }
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // The market-closed day owes nothing, so paying every other day fully settles the month.
        Assert.Equal(0m, summary.TotalOutstanding);
        Assert.Equal(1, summary.MonthsPaid);
        Assert.Equal(0, summary.MonthsUnpaid);
    }

    [Fact]
    public async Task Summary_Monthly_ExcusedMonth_OwesNothing()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", monthStart, 3, 1000m); // 1 collectable month
        var excused = StallMonthlyException.Create(stall.Id, today.Year, today.Month, MonthlyExceptionReason.TemporaryClosure, "Closed", "Head");
        context.AddRange(facility, stall, contract, excused);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // The excused month owes nothing and is neither paid nor unpaid.
        Assert.Equal(0m, summary.TotalOutstanding);
        Assert.Equal(0, summary.MonthsUnpaid);
        Assert.Equal(0m, summary.TotalCollected);
    }

    [Fact]
    public async Task Summary_Npm_RateChangedThisMonth_UsesCurrentRate_NotStartOfMonthRate()
    {
        // Regression: when a tenant changes its NPM daily rate mid-month (here ₱45 → ₱35 effective the 2nd),
        // the whole month must bill at the CURRENT (₱35, month-end) rate — matching the displayed rate and
        // the reports — not the ₱45 that was effective on the 1st. Distinctive rates (≠ the ₱30 ordinance
        // fallback) prove the seeded rows are actually resolved. Cantilan is single-rate, so it is unaffected.
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Funny Valentine", "Funny Valentine", monthStart, 3, 900m); // full current month, unpaid

        var rateOld = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 45m, new DateOnly(2020, 1, 1), Guid.Empty);
        var rateNew = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m, monthStart.AddDays(1), Guid.Empty);

        context.AddRange(facility, stall, contract, rateOld, rateNew);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var summary = await repo.GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // The days earned so far are charged at the rate this tenant now charges (₱35), not the ₱45 that was
        // effective on the 1st. The month in progress bills its elapsed days only — a whole month at ₱35 would be
        // ₱1,050, and that is what will be owed once the month is out.
        Assert.Equal(DomainRules.DailyBilledMonthObligation(35m, 0m, daysInMonth, today.Day), summary.TotalOutstanding);
        // Stated as "not the old rate" rather than as today's day count times 35: the month is capped at its rent, so
        // multiplying out by hand asserted ₱1,085 on the 31st of a 31-day month against an obligation of ₱1,050, and
        // failed on those days only. The line above already states the figure through the rule itself.
        Assert.NotEqual(DomainRules.DailyBilledMonthObligation(30m, 0m, daysInMonth, today.Day), summary.TotalOutstanding);        Assert.Equal(1_050m, DomainRules.DailyBilledMonthObligation(35m, 0m, daysInMonth, daysInMonth));
    }
}
