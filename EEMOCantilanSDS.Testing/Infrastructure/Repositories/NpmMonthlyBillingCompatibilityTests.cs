using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>Characterizes current compatibility hazards in isolated EF InMemory fixtures, not approved future rules.</summary>
public sealed class NpmMonthlyBillingCompatibilityTests : RepositoryTestBase
{
    [Fact]
    public async Task Current_basis_change_recalculates_old_unpaid_month_but_does_not_rewrite_collection()
    {
        await using var db = NewContext();
        var market = Facility.Create(FacilityCode.NPM, "Test market", "TEST");
        market.SetMonthBasis(NpmMonthBasis.PureDays);
        var stall = Stall.Create(market.Id, "A-1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Test occupant", "Test contract", new DateOnly(2025, 1, 1), 3, 900m);
        var cash = DailyCollection.Create(stall.Id, new DateOnly(2025, 2, 1), "Test", 30m);
        cash.MarkPaid("Historical-test-reference", null);
        db.AddRange(market, stall, contract, cash); await db.SaveChangesAsync();
        var before = await new FacilityReportsRepository(db).GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2025, 2, null, CancellationToken.None);
        market.SetMonthBasis(NpmMonthBasis.RentGoal); await db.SaveChangesAsync();
        var after = await new FacilityReportsRepository(db).GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2025, 2, null, CancellationToken.None);
        Assert.Equal(810m, Assert.Single(before.StallCompliance).Balance);
        Assert.Equal(870m, Assert.Single(after.StallCompliance).Balance);
        Assert.Equal(30m, cash.DailyFee);
        Assert.Equal("Historical-test-reference", cash.ORNumber);
        // The setting is not period-versioned. Task 1 must not flip existing facilities to retire PureDays.
    }

    [Fact]
    public async Task Current_limitation_annual_report_reuses_period_end_monthly_rate()
    {
        await using var db = NewContext();
        var market = Facility.Create(FacilityCode.NPM, "Test market", "TEST");
        var stall = Stall.Create(market.Id, "A-1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        db.AddRange(market, stall, Contract.Create(stall.Id, "Test occupant", "Test contract", new DateOnly(2025, 1, 1), 3, 900m),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 900m, new DateOnly(2025, 1, 1)),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1200m, new DateOnly(2025, 2, 1)));
        await db.SaveChangesAsync();
        var monthlySum = 0m;
        for (var month = 1; month <= 12; month++)
        {
            var report = await new FacilityReportsRepository(db).GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2025, month, null, CancellationToken.None);
            monthlySum += Assert.Single(report.StallCompliance).ExpectedBill;
        }
        var annual = await new FacilityReportsRepository(db).GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Yearly, 2025, null, null, CancellationToken.None);
        Assert.Equal(14100m, monthlySum);
        Assert.Equal(14400m, Assert.Single(annual.StallCompliance).ExpectedBill);
        // Pending acceptance: same-scope annual obligation should equal sum of effective-dated months. No engine fix here.
    }
}
