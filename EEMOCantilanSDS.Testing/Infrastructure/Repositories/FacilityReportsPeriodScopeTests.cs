using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A stall must only appear in a report for periods during which its contract was actually
/// effective. Regression: navigating to May 2026 (before contracts began Jun 5) showed all four
/// stalls as phantom "Paid" payors — their ₱0 May obligation rendered as fully collected.
/// </summary>
public class FacilityReportsPeriodScopeTests : RepositoryTestBase
{
    private static async Task SeedFourJune5StallsAsync(EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var s1 = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var s2 = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var s3 = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var s4 = Stall.Create(facility.Id, "4", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var start = new DateOnly(2026, 6, 5);
        context.AddRange(facility, s1, s2, s3, s4,
            Contract.Create(s1.Id, "Pantom Dant", "Pantom Dant", start, 3, 900m),
            Contract.Create(s2.Id, "Ana Reyes", "Ana Reyes", start, 3, 900m),
            Contract.Create(s3.Id, "Lorna Buenades", "Lorna Buenades", start, 3, 900m),
            Contract.Create(s4.Id, "Delta Rall", "Delta Rall", start, 3, 900m));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task MonthlyReport_BeforeContractsBegin_ShowsNoStallsOrPayors()
    {
        var context = NewContext();
        await SeedFourJune5StallsAsync(context);

        var repo = new FacilityReportsRepository(context);
        var may = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 5, null, CancellationToken.None);

        Assert.Empty(may.StallCompliance);
        Assert.Equal(0, may.OccupiedStalls);
        Assert.Equal(4, may.TotalStalls); // the physical stalls still exist
        Assert.Equal(0m, may.PendingPaymentAmount);
        Assert.Equal(0, may.PendingPaymentCount);
        Assert.Equal(0, may.CollectionPerformance.FullyPaidCount);
        Assert.Equal(0, may.CollectionPerformance.PartiallyPaidCount);
        Assert.Equal(0, may.CollectionPerformance.UnpaidCount);
        Assert.All(may.SectionBreakdown, s => Assert.Equal(0, s.ActiveStalls));
    }

    [Fact]
    public async Task YearlyReport_YearBeforeContracts_ShowsNoStalls()
    {
        var context = NewContext();
        await SeedFourJune5StallsAsync(context);

        var repo = new FacilityReportsRepository(context);
        var year2025 = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Yearly, 2025, null, null, CancellationToken.None);

        Assert.Empty(year2025.StallCompliance);
        Assert.Equal(0, year2025.OccupiedStalls);
    }

    [Fact]
    public async Task MonthlyReport_PeriodWhenContractsAreEffective_ShowsTheStalls()
    {
        // Sanity: the in-scope month (June) is unaffected by the period-scope filter.
        var context = NewContext();
        await SeedFourJune5StallsAsync(context);

        var repo = new FacilityReportsRepository(context);
        var june = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Equal(4, june.StallCompliance.Count);
        Assert.Equal(4, june.OccupiedStalls);
    }
}
