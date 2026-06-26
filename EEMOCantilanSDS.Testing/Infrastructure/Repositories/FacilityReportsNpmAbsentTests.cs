using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// NPM "Absent" recognition: an excused/absent day is fully excused — it owes ₱0, drops out of the
/// obligation and the missed/coverage math, and a stall whose every collectable day is excused reads
/// as the distinct "Absent" status rather than "Unpaid".
/// </summary>
public class FacilityReportsNpmAbsentTests : RepositoryTestBase
{
    [Fact]
    public async Task MonthlyCompliance_AbsentDays_AreExcused_ReduceObligation_NotOwed()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 1, 1), 3, 900m);
        context.AddRange(facility, stall, contract);

        // Five excused/absent days in June 2026 (5–9); nothing paid.
        for (var day = 5; day <= 9; day++)
        {
            var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            absent.MarkAbsent();
            context.Add(absent);
        }
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(750m, c.ExpectedBill);  // (30 − 5 excused) × ₱30 — the absent days are not owed
        Assert.Equal(750m, c.Balance);        // balance reflects only the 25 non-excused days
        Assert.Equal(5, c.AbsentDays);
        Assert.Equal("Unpaid", c.Status);     // still owes the non-excused days
    }

    [Fact]
    public async Task MonthlyCompliance_AllCollectableDaysAbsent_YieldsAbsentStatus_ZeroOwed()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Effective only the last 3 days of June → exactly 3 collectable days.
        var contract = Contract.Create(stall.Id, "Ben Cruz", "Ben Cruz", new DateOnly(2026, 6, 28), 3, 900m);
        context.AddRange(facility, stall, contract);

        foreach (var day in new[] { 28, 29, 30 })
        {
            var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            absent.MarkAbsent();
            context.Add(absent);
        }
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(0m, c.ExpectedBill);
        Assert.Equal(0m, c.Balance);
        Assert.Equal(3, c.AbsentDays);
        Assert.Equal("Absent", c.Status);
        // An excused stall must not surface as an open receivable / delinquent.
        Assert.DoesNotContain(report.StallCompliance, x => x.Status == "Absent" && x.Balance > 0m);
    }

    [Fact]
    public async Task MonthlyCompliance_PaidDespiteSomeAbsent_StaysPaid_WhenRemainingDaysCovered()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Effective the last 5 days of June → 5 collectable days.
        var contract = Contract.Create(stall.Id, "Cita Lim", "Cita Lim", new DateOnly(2026, 6, 26), 3, 900m);
        context.AddRange(facility, stall, contract);

        // 2 excused days + 3 paid days = the whole 5-day occupancy accounted for → Paid, ₱0 balance.
        foreach (var day in new[] { 26, 27 })
        {
            var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            absent.MarkAbsent();
            context.Add(absent);
        }
        foreach (var day in new[] { 28, 29, 30 })
        {
            var paid = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            paid.MarkPaid($"OR-{day}", Guid.NewGuid());
            context.Add(paid);
        }
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(90m, c.ExpectedBill);   // (5 − 2 excused) × ₱30
        Assert.Equal(90m, c.AmountPaid);      // 3 paid days × ₱30
        Assert.Equal(0m, c.Balance);
        Assert.Equal(2, c.AbsentDays);
        Assert.Equal("Paid", c.Status);
    }
}
