using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Collection Trend bar splits each period into its daily-rent base and the fish-kilo (₱1/kg)
/// fee, so RevenueTrend must expose the fish portion per period.
/// </summary>
public class FacilityReportsNpmTrendTests : RepositoryTestBase
{
    [Fact]
    public async Task MonthlyTrend_Npm_SplitsFishFeeFromDailyRent()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Lorna", "Lorna", monthStart.AddMonths(-1), 3, 900m);

        // Two paid days this month with fish kilos: 5kg + 3kg = ₱8 fish, plus ₱60 daily rent.
        var d1 = DailyCollection.Create(stall.Id, monthStart); d1.MarkPaid("OR-1", Guid.NewGuid(), fishKilos: 5m);
        var d2 = DailyCollection.Create(stall.Id, monthStart.AddDays(1)); d2.MarkPaid("OR-2", Guid.NewGuid(), fishKilos: 3m);

        context.AddRange(facility, stall, contract, d1, d2);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, today.Year, today.Month, null, CancellationToken.None);

        var current = report.RevenueTrend.Single(t => t.IsCurrentPeriod);
        Assert.Equal(8m, current.FishFeeRevenue);   // 8 kg × ₱1
        Assert.Equal(68m, current.Revenue);         // ₱60 daily rent + ₱8 fish
    }
}
