using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A market space is let for a MONTHLY rent — ₱900 on the office's own List of Stallholders, ₱10,800 for a complete
/// year — and the ₱30 daily fee is the installment that rent is collected in, not the measure of it. Every figure the
/// office reads comes from that one monthly ledger: Expected (the obligation), Collected (installments received),
/// Credits (days nothing is owed for), and Outstanding = Expected − Collected − Credits.
/// </summary>
public class NpmMonthlyObligationLedgerTests : RepositoryTestBase
{
    private const decimal Fee = FeeRates.NpmDailyFee;                            // ₱30 installment
    private static decimal MonthlyRent => Fee * DomainRules.DailyBilledMonthDays;  // ₱900 obligation

    [Theory]
    [InlineData(31, 31, 900)]   // a 31-day month held in full owes the rent, not 31 installments
    [InlineData(30, 30, 900)]
    [InlineData(28, 28, 900)]   // February owes the same rent as any other month
    [InlineData(29, 29, 900)]   // a leap February too
    [InlineData(31, 12, 360)]   // held only part of the month → the days held, one installment each
    [InlineData(28, 20, 600)]
    [InlineData(31, 0, 0)]
    public void TheMonthsObligation_IsTheRentWhenHeldInFull_AndTheDaysHeldOtherwise(
        int daysInMonth, int daysHeld, decimal expected)
    {
        // No stated monthly rent (0): the month is thirty of the LGU's own daily fee.
        Assert.Equal(expected, DomainRules.DailyBilledMonthObligation(Fee, 0m, daysInMonth, daysHeld));
    }

    [Fact]
    public void AnLguThatStatesItsOwnMonthlyRent_IsBilledThatRent()
    {
        // The ordinance an LGU actually passed: ₱35 a day and ₱1,000 a month. Thirty installments would be ₱1,050,
        // which is not what its paper says, so the stated month wins — for every complete month, and for the year.
        Assert.Equal(1_000m, DomainRules.DailyBilledMonthObligation(35m, 1_000m, 31, 31));
        Assert.Equal(1_000m, DomainRules.DailyBilledMonthObligation(35m, 1_000m, 28, 28));
        Assert.Equal(12_000m, Enumerable.Range(1, 12)
            .Sum(m => DomainRules.DailyBilledMonthObligation(35m, 1_000m, DateTime.DaysInMonth(2026, m), DateTime.DaysInMonth(2026, m))));

        // A part-month is still the days held, one installment each — and never more than that month's rent.
        Assert.Equal(350m, DomainRules.DailyBilledMonthObligation(35m, 1_000m, 31, 10));
        Assert.Equal(1_000m, DomainRules.DailyBilledMonthObligation(35m, 1_000m, 31, 30));
    }

    [Fact]
    public void TwelveCompleteMonths_Owe10800()
    {
        var year = 0m;
        for (var month = 1; month <= 12; month++)
        {
            var daysInMonth = DateTime.DaysInMonth(2026, month);
            year += DomainRules.DailyBilledMonthObligation(Fee, 0m, daysInMonth, daysInMonth);
        }

        Assert.Equal(10_800m, year);
    }

    [Fact]
    public void ATenantsOwnRate_SetsItsOwnMonthlyObligation()
    {
        // ₱40/day is a ₱1,200 month and ₱14,400 a year; a custom section on ₱50 is ₱1,500. Nothing is hardcoded.
        Assert.Equal(1_200m, DomainRules.DailyBilledMonthObligation(40m, 0m, 31, 31));
        Assert.Equal(1_500m, DomainRules.DailyBilledMonthObligation(50m, 0m, 28, 28));
    }

    [Theory]
    [InlineData(31, 10, 300)]   // ten absent days credit ten installments
    [InlineData(28, 28, 900)]   // a month never traded owes nothing at all — the whole obligation is credited
    [InlineData(31, 0, 0)]
    public void CreditsForgiveWhatIsNotOwed(int daysHeld, int daysForgiven, decimal expected)
    {
        var obligation = DomainRules.DailyBilledMonthObligation(Fee, 0m, daysHeld, daysHeld);
        Assert.Equal(expected, DomainRules.DailyBilledMonthCredit(Fee, obligation, daysHeld, daysForgiven));
    }

    [Fact]
    public void OverCollectionIsRevenue_NotANegativeOutstanding()
    {
        // A 31st installment collected at the stall is income beyond the rent; it can bring a month to nil, never
        // below it, so Outstanding never turns into a credit note.
        Assert.Equal(0m, DomainRules.DailyBilledMonthOutstanding(MonthlyRent, 930m, 0m));
        Assert.Equal(600m, DomainRules.DailyBilledMonthOutstanding(MonthlyRent, 300m, 0m));
        Assert.Equal(300m, DomainRules.DailyBilledMonthOutstanding(MonthlyRent, 300m, 300m));
    }

    private static (Facility Facility, Stall Stall, Contract Term) NpmStall(DateOnly from)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var term = Contract.Create(stall.Id, "Merlita A. Abuso", "Merlita A. Abuso", from, 3, 900m);
        return (facility, stall, term);
    }

    [Theory]
    [InlineData(2)]    // February — 28 days, the month whose installments cannot reach the rent
    [InlineData(4)]    // April — 30 days
    [InlineData(8)]    // August — 31 days, the month that used to read ₱930
    public async Task EveryMonthsReportedObligation_IsTheRent(int month)
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, month, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(MonthlyRent, row.ExpectedBill);
        Assert.Equal(MonthlyRent, row.Balance);
        Assert.Equal("Unpaid", row.Status);
    }

    [Fact]
    public async Task AWholeYear_Owes10800()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(10_800m, row.ExpectedBill);
        Assert.Equal(10_800m, row.Balance);
    }

    [Fact]
    public async Task Expected_LessCollected_LessCredits_IsOutstanding()
    {
        // The identity, on one month's ledger: ₱900 expected, ten days collected (₱300), five days credited (₱150),
        // and the ₱450 that remains.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 10; day++)
        {
            var paid = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            paid.MarkPaid(string.Empty, collectorId: null);
            context.Add(paid);
        }
        for (var day = 11; day <= 15; day++)
        {
            var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            absent.MarkAbsent("Head");
            context.Add(absent);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        const decimal collected = 10 * Fee;
        const decimal credits = 5 * Fee;

        Assert.Equal(MonthlyRent - credits, row.ExpectedBill);      // the obligation, net of what is forgiven
        Assert.Equal(collected, row.AmountPaid);
        Assert.Equal(MonthlyRent - collected - credits, row.Balance);
        Assert.Equal(450m, row.Balance);
        Assert.Equal("Partial", row.Status);
    }

    [Fact]
    public async Task AMonthNeverTraded_OwesNothing()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 30; day++)
        {
            var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 4, day));
            absent.MarkAbsent("Head");
            context.Add(absent);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 4, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(0m, row.ExpectedBill);
        Assert.Equal(0m, row.Balance);
        Assert.Equal("Absent", row.Status);
    }

    [Fact]
    public async Task OnceTheRentIsIn_TheMonthIsSettled_AndAnyFurtherDayIsRevenue()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 31; day++)
        {
            var paid = DailyCollection.Create(stall.Id, new DateOnly(2026, 8, day));
            paid.MarkPaid(string.Empty, collectorId: null);
            context.Add(paid);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(MonthlyRent, row.ExpectedBill);
        Assert.Equal(31 * Fee, row.AmountPaid);       // ₱930 received — the day-to-day truth is kept
        Assert.Equal(0m, row.Balance);                // and never a negative balance
        Assert.Equal("Paid", row.Status);
    }

    [Fact]
    public async Task AMidMonthStart_OwesOnlyTheDaysHeld()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 8, 20));   // twelve days of August
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(12 * Fee, row.ExpectedBill);
        Assert.True(row.ExpectedBill < MonthlyRent);
    }

    [Fact]
    public async Task TheInactiveRegister_ReadsTheSameLedger()
    {
        // A lapsed daily account's arrears are months of rent: a complete year is ₱10,800, not 365 installments.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var lapsed = Contract.Create(stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", new DateOnly(2026, 1, 1), 1, 900m);
        lapsed.Terminate("Head", new DateOnly(2026, 12, 31));

        context.AddRange(facility, stall, lapsed);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None));

        Assert.Equal(10_800m, row.Uncollected);
    }

    [Fact]
    public async Task AClosedShortMonth_SettledWithItsAdjustment_ReadsAsPaid()
    {
        // February's twenty-eight installments (₱840) plus its ₱60 month-end adjustment meet the ₱900 rent, so the
        // month is settled — the ledger balances and nothing is left that no day could ever clear.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        DailyCollection? last = null;
        for (var day = 1; day <= 28; day++)
        {
            last = DailyCollection.Create(stall.Id, new DateOnly(2026, 2, day));
            last.MarkPaid(string.Empty, collectorId: null);
            context.Add(last);
        }
        last!.AddMonthEndAdjustment(60m, "Admin");

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 2, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(MonthlyRent, row.ExpectedBill);
        Assert.Equal(MonthlyRent, row.AmountPaid);
        Assert.Equal(0m, row.Balance);
        Assert.Equal("Paid", row.Status);
    }

    [Fact]
    public async Task AClosedShortMonth_LeftAtItsInstallments_StillOwesTheAdjustment()
    {
        // Every day of February collected but no adjustment taken: the ₱60 remains outstanding, and — the month
        // having closed — it is properly collectible arrears rather than a figure nobody can act on.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        context.AddRange(facility, stall, term);

        for (var day = 1; day <= 28; day++)
        {
            var paid = DailyCollection.Create(stall.Id, new DateOnly(2026, 2, day));
            paid.MarkPaid(string.Empty, collectorId: null);
            context.Add(paid);
        }

        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 2, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(28 * Fee, row.AmountPaid);
        Assert.Equal(60m, row.Balance);
        Assert.Equal("Partial", row.Status);
    }

    [Fact]
    public async Task TheMonthInProgress_IsNotArrears()
    {
        // The current month has not fallen due, so nothing in it — least of all a month-end shortfall — may be read
        // as arrears. The delinquency window starts at the month before this one.
        var context = NewContext();
        var today = PhilippineTime.Today;
        var (facility, stall, term) = NpmStall(new DateOnly(today.Year, today.Month, 1));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var delinquents = await new FacilityReportsRepository(context)
            .GetDelinquentStallsAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        Assert.Empty(delinquents);
    }

    [Fact]
    public async Task AnLgusStatedMonthlyRent_DrivesTheReportedObligation()
    {
        // An LGU whose ordinance says ₱35 a day and ₱1,000 a month: the month owes the ₱1,000 it passed, not the
        // ₱1,085 its calendar would make nor the ₱1,050 thirty installments would. Nothing about Cantilan changes —
        // it states no monthly rent, so its month stays thirty of its ₱30.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        var daily = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m, new DateOnly(2020, 1, 1), Guid.Empty);
        var monthly = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_000m, new DateOnly(2020, 1, 1), Guid.Empty);

        context.AddRange(facility, stall, term, daily, monthly);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(1_000m, row.ExpectedBill);
        Assert.Equal(1_000m, row.Balance);

        var year = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);
        Assert.Equal(12_000m, Assert.Single(year.StallCompliance).ExpectedBill);
    }

    [Fact]
    public async Task ACustomSectionsOwnRate_DecidesItsOwnMonth_NotTheLgusStatedOne()
    {
        // A custom section is priced by its own daily rate, so the market-wide monthly rent an LGU states for its
        // canonical sections must not be applied to a section it does not price.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "S1", 900m, ApplicableFees.DailyRental,
            section: null, dailyRate: 50m, customSectionName: "Sari-sari Area");
        var term = Contract.Create(stall.Id, "Custom Lessee", "Custom Lessee", new DateOnly(2026, 1, 1), 3, 900m);
        var monthly = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_000m, new DateOnly(2020, 1, 1), Guid.Empty);

        context.AddRange(facility, stall, term, monthly);
        await context.SaveChangesAsync();

        var report = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);

        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(1_500m, row.ExpectedBill);   // ₱50 × 30, its own rate — not the stated ₱1,000
    }

    [Fact]
    public async Task TheRosterStatesTheLgusOwnMonthlyRent()
    {
        // The "Monthly Rentals per Contract" column on the official sheet: the rent the LGU passed, and twelve of
        // them for the year.
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2026, 1, 1));
        var daily = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m, new DateOnly(2020, 1, 1), Guid.Empty);
        var monthly = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_000m, new DateOnly(2020, 1, 1), Guid.Empty);

        context.AddRange(facility, stall, term, daily, monthly);
        await context.SaveChangesAsync();

        var roster = await new StallRepository(context).GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        var row = Assert.Single(roster.Sections.SelectMany(s => s.Rows));
        Assert.Equal(1_000m, row.MonthlyRentalRate);
        Assert.Equal(12_000m, row.WholeYearRental);
    }

    [Fact]
    public async Task AMonthWhoseTermRanOutPartWay_IsChargedItsDays_NotTheWholeRent()
    {
        // A term that lapses on the 7th leaves seven days of that month owed — ₱210, not the month's ₱900. Measuring
        // the calendar from the term's own last day made those seven days look like a month held in full, which is
        // exactly the shape of Cantilan's own market terms (expiry on the 7th).
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);

        var today = PhilippineTime.Today;
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lapsed = new DateOnly(lastMonth.Year, lastMonth.Month, 7);

        // Three years to the 7th of last month, and nothing ever collected.
        var term = Contract.Create(stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", lapsed.AddYears(-3), 3, 900m);
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var summary = await new PaymentRepository(context).GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // Ten whole months of rent inside the twelve-month window, plus the seven days of the month it lapsed —
        // the month after it owes nothing at all, the term having ended.
        Assert.Equal((10 * MonthlyRent) + (7 * Fee), summary.TotalOutstanding);
        Assert.Equal(9_210m, summary.TotalOutstanding);
    }

    [Fact]
    public async Task TheCollectorsOwnReport_AssessesTheSameMonthAsTheOffice()
    {
        // The mobile report exists to reconcile with the web's figures, so both read the monthly obligation.
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
        Assert.Equal(MonthlyRent, payee.AssessedAmount);

        var office = await new FacilityReportsRepository(context)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 8, null, CancellationToken.None);
        Assert.Equal(Assert.Single(office.StallCompliance).Balance, payee.Balance);
    }
}
