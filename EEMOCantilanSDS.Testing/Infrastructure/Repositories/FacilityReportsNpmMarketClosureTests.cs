using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// NPM market closure: a facility-wide closed day excuses EVERY payor for that date (₱0 owed, dropped
/// from the obligation and the missed/coverage math) without any per-stall DailyCollection row. A stall
/// whose every collectable day is closed reads as "Absent".
/// </summary>
public class FacilityReportsNpmMarketClosureTests : RepositoryTestBase
{
    [Fact]
    public async Task ClosedDays_AreExcused_ReduceObligation_NotOwed()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 1, 1), 3, 900m);
        context.AddRange(facility, stall, contract);

        // Market closed June 5–9 (5 days). No per-stall DailyCollection rows exist for these days.
        for (var day = 5; day <= 9; day++)
            context.Add(NpmMarketClosure.Create(new DateOnly(2026, 6, day), MarketClosureReason.Holiday));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(750m, c.ExpectedBill);  // (30 − 5 closed) × ₱30
        Assert.Equal(750m, c.Balance);
        Assert.Equal(5, c.AbsentDays);
        Assert.Equal("Unpaid", c.Status);     // still owes the open days
    }

    [Fact]
    public async Task AllCollectableDaysClosed_YieldsAbsentStatus_ZeroOwed()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Effective only the last 3 days of June → exactly 3 collectable days.
        var contract = Contract.Create(stall.Id, "Ben Cruz", "Ben Cruz", new DateOnly(2026, 6, 28), 3, 900m);
        context.AddRange(facility, stall, contract);

        foreach (var day in new[] { 28, 29, 30 })
            context.Add(NpmMarketClosure.Create(new DateOnly(2026, 6, day)));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(0m, c.ExpectedBill);
        Assert.Equal(0m, c.Balance);
        Assert.Equal(3, c.AbsentDays);
        Assert.Equal("Absent", c.Status);
        Assert.DoesNotContain(report.StallCompliance, x => x.Status == "Absent" && x.Balance > 0m);
    }

    [Fact]
    public async Task Closure_IsFacilityWide_ExcusesEveryPayor()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var s1 = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var s2 = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var c1 = Contract.Create(s1.Id, "Ana", "Ana", new DateOnly(2026, 1, 1), 3, 900m);
        var c2 = Contract.Create(s2.Id, "Ben", "Ben", new DateOnly(2026, 1, 1), 3, 900m);
        context.AddRange(facility, s1, s2, c1, c2);

        // A single closure row for June 15 must excuse BOTH stalls.
        context.Add(NpmMarketClosure.Create(new DateOnly(2026, 6, 15)));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Equal(2, report.StallCompliance.Count);
        Assert.All(report.StallCompliance, c =>
        {
            Assert.Equal(870m, c.ExpectedBill);  // (30 − 1 closed) × ₱30 for every payor
            Assert.Equal(1, c.AbsentDays);
        });
    }
}
