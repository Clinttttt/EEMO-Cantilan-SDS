using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The market is collected day by day, but the space is let for a monthly rent. A 31-day month was raising a debt of
/// ₱930 against a payor whose rent is ₱900 — the figure the office's own paper states, and the one it reconciles
/// against. A month therefore owes its collectable days at the day's rate but never more than that rent: once the
/// rent is in, the month is paid. The rule caps and never tops up, so a short month, a mid-month start and excused
/// days all owe only their own days; and collection stays day by day, so a 31st day actually traded may still be
/// received — it is revenue beyond the rent, not an arrear.
/// </summary>
public class NpmMonthlyBaseRentCapTests : RepositoryTestBase
{
    private const decimal Fee = FeeRates.NpmDailyFee;               // ₱30
    private static decimal BaseRent => Fee * DomainRules.DailyBilledMonthDays;   // ₱900

    [Theory]
    [InlineData(31, 900)]   // a 31-day month owes the rent, not ₱930
    [InlineData(30, 900)]   // a 30-day month is exactly the rent
    [InlineData(28, 840)]   // February owes its own days — the rule never tops up
    [InlineData(20, 600)]   // a mid-month start owes only the days held
    [InlineData(0, 0)]
    public void TheMonthsCharge_IsItsDays_ButNeverMoreThanTheRent(int billableDays, decimal expected)
    {
        Assert.Equal(expected, DomainRules.DailyBilledMonthCharge(Fee, billableDays));
    }

    [Fact]
    public void ATenantsOwnRate_IsCappedAtThatRatesMonthlyRent()
    {
        // A municipality on ₱40/day has a ₱1,200 month; a custom section on ₱50 has ₱1,500. The cap is always the
        // space's own rate over a 30-day month, never a hardcoded figure.
        Assert.Equal(1_200m, DomainRules.DailyBilledMonthCharge(40m, 31));
        Assert.Equal(1_500m, DomainRules.DailyBilledMonthCharge(50m, 31));
    }

    private static (Facility Facility, Stall Stall, Contract Term) NpmStall(DateOnly from)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var term = Contract.Create(stall.Id, "Merlita A. Abuso", "Merlita A. Abuso", from, 3, 900m);
        return (facility, stall, term);
    }

    [Fact]
    public async Task ReportedBalance_ForA31DayMonth_IsTheRent_NotAnExtraDay()
    {
        // August 2026 has 31 days. The follow-up queue and the report's delinquency table read this figure, which
        // was showing ₱930 against a ₱900 stall.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(BaseRent, row.ExpectedBill);
        Assert.Equal(BaseRent, row.Balance);
        Assert.Equal("Unpaid", row.Status);
    }

    [Fact]
    public async Task OnceTheRentIsIn_TheMonthIsPaid_AndTheExtraDayIsRevenueNotABalance()
    {
        // Thirty days collected settles the rent. The 31st day is still traded and still collected — the office may
        // receive it, and it counts as collected — but the month owes nothing more and never goes below nil.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 31; day++)
        {
            var daily = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            daily.MarkPaid(string.Empty, collectorId: null);
            context.Add(daily);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(BaseRent, row.ExpectedBill);
        Assert.Equal(31 * Fee, row.AmountPaid);      // ₱930 actually received — the day-to-day truth is kept
        Assert.Equal(0m, row.Balance);               // …and never a negative balance
        Assert.Equal("Paid", row.Status);
    }

    [Fact]
    public async Task ThirtyDaysCollectedInA31DayMonth_AlreadySettlesIt()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 30; day++)
        {
            var daily = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            daily.MarkPaid(string.Empty, collectorId: null);
            context.Add(daily);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(0m, row.Balance);
        Assert.Equal("Paid", row.Status);
    }

    [Fact]
    public async Task APartlyCollectedMonth_OwesTheRestOfTheRent_NotTheRestOfTheDays()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 10; day++)
        {
            var daily = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            daily.MarkPaid(string.Empty, collectorId: null);
            context.Add(daily);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(300m, row.AmountPaid);
        Assert.Equal(600m, row.Balance);             // ₱900 rent less ₱300 in, not ₱930 less ₱300
        Assert.Equal("Partial", row.Status);
    }

    [Fact]
    public async Task ExcusedDays_StillReduceTheMonth_BecauseTheRuleOnlyCaps()
    {
        // Ten absent days in a 31-day month: twenty-one days owed at ₱30 = ₱630, which is under the rent, so the
        // cap does not apply. A payor who did not trade is never charged the full rent for the month.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 10; day++)
        {
            var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            absent.MarkAbsent("Head");
            context.Add(absent);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(630m, row.ExpectedBill);
        Assert.Equal(630m, row.Balance);
    }

    [Fact]
    public async Task AWholeYear_CapsEachMonthOnItsOwn()
    {
        // Eleven capped months plus February's own twenty-eight days: ₱10,740, not ₱10,950 (365 days × ₱30).
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(10_740m, row.ExpectedBill);
        Assert.True(row.ExpectedBill < 365 * Fee);
    }

    [Fact]
    public async Task TheCollectorsOwnReport_AssessesTheSameMonthAsTheOffice()
    {
        // The mobile report exists to reconcile with the web's figures. It assessed days × ₱30 while the office's
        // screen capped the month at its rent, so a 31-day month read ₱930 in the collector's hand and ₱900 on the
        // office's screen — the one disagreement this figure must never have.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", "pw");
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));

        context.AddRange(facility, stall, term, collector);
        await context.SaveChangesAsync();

        var report = await new CollectorRepository(context).GetCollectorReportAsync(
            collector.Id,
            new[] { FacilityCode.NPM },
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        var payee = Assert.Single(report.Payees);
        Assert.Equal(BaseRent, payee.AssessedAmount);
        Assert.Equal(BaseRent, payee.Balance);

        var office = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);
        Assert.Equal(Assert.Single(office.StallCompliance).Balance, payee.Balance);
    }

    [Fact]
    public async Task TheInactiveRegister_ChargesAMonthNoMoreThanItsRent()
    {
        // The same rule on the closed/inactive register: a lapsed daily account's arrears are months of rent, so a
        // year of them reads ₱10,740 rather than ₱10,950.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var lapsed = Contract.Create(stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", new DateOnly(2026, 1, 1), 1, 900m);
        lapsed.Terminate("Head", new DateOnly(2026, 12, 31));

        context.AddRange(facility, stall, lapsed);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None));

        Assert.Equal(10_740m, row.Uncollected);
    }
}
