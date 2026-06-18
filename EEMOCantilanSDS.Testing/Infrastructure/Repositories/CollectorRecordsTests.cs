using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// The mobile Records feed (<see cref="CollectorRepository.GetCollectorRecordsAsync"/>) must return only
/// the authenticated collector's OWN collection events, scoped by facility + PH date range, with the
/// correct paid/partial amounts. These lock in isolation, filtering, and money accuracy.
/// </summary>
public class CollectorRecordsTests : RepositoryTestBase
{
    private static readonly DateOnly Today = PhilippineTime.Today;

    [Fact]
    public async Task ReturnsOwnRecordsOnly_AndRespectsFacilityFilter()
    {
        await using var ctx = NewContext();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();

        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Owner A", 1, "OR-A", Today));     // mine
        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), other, "Owner B", 1, "OR-B", Today));  // someone else's
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        var all = await repo.GetCollectorRecordsAsync(me, null, Today, Today, CancellationToken.None);
        var mine = Assert.Single(all);
        Assert.Equal("Owner A", mine.PayorName);
        Assert.Equal(FacilityCode.SLH, mine.FacilityCode);
        Assert.Equal(250m, mine.Amount);
        Assert.False(mine.IsPartial);

        Assert.Single(await repo.GetCollectorRecordsAsync(me, FacilityCode.SLH, Today, Today, CancellationToken.None));
        Assert.Empty(await repo.GetCollectorRecordsAsync(me, FacilityCode.TRM, Today, Today, CancellationToken.None));
    }

    [Fact]
    public async Task RespectsDateRange()
    {
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Recent", 1, "OR-1", Today));
        ctx.Add(SlaughterTransaction.CreateHog(Guid.NewGuid(), me, "Old", 1, "OR-2", Today.AddDays(-40)));
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        var todayOnly = await repo.GetCollectorRecordsAsync(me, null, Today, Today, CancellationToken.None);
        Assert.Equal("Recent", Assert.Single(todayOnly).PayorName);

        var oldDay = Today.AddDays(-40);
        var oldOnly = await repo.GetCollectorRecordsAsync(me, null, oldDay, oldDay, CancellationToken.None);
        Assert.Equal("Old", Assert.Single(oldOnly).PayorName);
    }

    [Fact]
    public async Task MonthlyPartialPayment_MapsFullAndCollectedAmounts()
    {
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "B-1", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2025, 1, 1), 3, 2400m);
        var payment = PaymentRecord.Create(stall.Id, Today.Year, Today.Month, 2400m);
        payment.UpdateStatus(PaymentStatus.Partial, 1000m, null, "tester", me);

        ctx.AddRange(facility, stall, contract, payment);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var rec = Assert.Single(await repo.GetCollectorRecordsAsync(me, null, monthStart, Today, CancellationToken.None));

        Assert.Equal("Maria Santos", rec.PayorName);
        Assert.Equal(FacilityCode.TCC, rec.FacilityCode);
        Assert.True(rec.IsPartial);
        Assert.Equal(2400m, rec.Amount);       // full bill
        Assert.Equal(1000m, rec.AmountPaid);   // collected
    }

    [Fact]
    public async Task CollectorReport_NpmIncludesMonthlyPayments_AndDoesNotDoubleCountDailyRowsForSameStall()
    {
        await using var ctx = NewContext();
        var collectorId = Guid.NewGuid();
        var reportDay = new DateOnly(Today.Year, Today.Month, 1);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var monthlyStall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var dailyStall = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var missedStall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(monthlyStall, facility);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(dailyStall, facility);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(missedStall, facility);
        var monthlyContract = Contract.Create(monthlyStall.Id, "Monthly Payor", "Monthly Payor", reportDay.AddMonths(-1), 3, 900m);
        var dailyContract = Contract.Create(dailyStall.Id, "Daily Payor", "Daily Payor", reportDay.AddMonths(-1), 3, 900m);
        var missedContract = Contract.Create(missedStall.Id, "Unpaid Payor", "Unpaid Payor", reportDay.AddMonths(-1), 3, 900m);
        monthlyStall.Contracts.Add(monthlyContract);
        dailyStall.Contracts.Add(dailyContract);
        missedStall.Contracts.Add(missedContract);

        var monthlyPayment = PaymentRecord.Create(monthlyStall.Id, reportDay.Year, reportDay.Month, 900m);
        monthlyPayment.RecordPayment("OR-MONTHLY", collectorId, PaymentStatus.Partial, partialAmount: 80m);

        var duplicateDaily = DailyCollection.Create(monthlyStall.Id, reportDay);
        duplicateDaily.MarkPaid("OR-DAILY-DUP", collectorId);
        monthlyStall.DailyCollections.Add(duplicateDaily);

        var dailyCollection = DailyCollection.Create(dailyStall.Id, reportDay);
        dailyCollection.MarkPaid("OR-DAILY", collectorId);
        dailyStall.DailyCollections.Add(dailyCollection);

        ctx.AddRange(
            facility,
            monthlyStall, dailyStall, missedStall,
            monthlyContract, dailyContract, missedContract,
            monthlyPayment, duplicateDaily, dailyCollection);
        await ctx.SaveChangesAsync();

        Assert.Equal(3, await ctx.Stalls
            .Where(s => s.Facility!.Code == FacilityCode.NPM && s.Contracts.Any(c => c.IsActive))
            .CountAsync());
        Assert.Single(await ctx.PaymentRecords
            .Where(p => p.StallId == monthlyStall.Id && p.Status == PaymentStatus.Partial)
            .ToListAsync());
        var npmStallIds = await ctx.Stalls
            .AsNoTracking()
            .Where(s => s.Facility!.Code == FacilityCode.NPM && s.Contracts.Any(c => c.IsActive))
            .Select(s => s.Id)
            .ToListAsync();
        Assert.Single(await ctx.PaymentRecords
            .AsNoTracking()
            .Where(p => npmStallIds.Contains(p.StallId))
            .ToListAsync());

        var repo = new CollectorRepository(ctx);
        var report = await repo.GetCollectorReportAsync(
            collectorId,
            [FacilityCode.NPM],
            reportDay,
            reportDay.AddDays(2),
            CancellationToken.None);

        Assert.Equal(110m, report.Totals.CollectedAmount); // monthly partial ₱80 + one daily ₱30
        Assert.Equal(2, report.Totals.TransactionCount);

        var period = Assert.Single(report.Periods, p => p.PeriodDate == reportDay);
        Assert.Equal(30m, period.CollectedAmount);
        Assert.Equal(1, period.OpenItemCount);
    }

}
