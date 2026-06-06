using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The NPM operational table is driven by daily-collection status (paid today / days paid /
/// last paid), not by monthly payment records — because NPM collects ₱30/day.
/// </summary>
public class PaymentRepositoryNpmDailyStatusTests : RepositoryTestBase
{
    [Fact]
    public async Task DailyStatus_ReportsPaidToday_DaysPaid_AndLastPaid()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 1, 1), 3, 900m);

        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        // Three paid days this month, including today.
        var days = new[] { monthStart, monthStart.AddDays(1), today }.Distinct().ToArray();
        var collections = days.Select(d =>
        {
            var dc = DailyCollection.Create(stall.Id, d);
            dc.MarkPaid($"OR-{d:yyyyMMdd}", Guid.NewGuid());
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, contract);
        context.AddRange(collections);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        var row = Assert.Single(status);
        Assert.Equal(stall.Id, row.StallId);
        Assert.True(row.PaidToday);
        Assert.Equal(days.Length, row.DaysPaidThisMonth);
        Assert.Equal(today, row.LastPaidDate);
    }

    [Fact]
    public async Task DailyStatus_StallWithNoCollections_IsAbsent()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var today = PhilippineTime.Today;
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        // No paid daily collections → stall does not appear; the page treats it as "Not yet / 0 days".
        Assert.Empty(status);
    }

    [Fact]
    public async Task DailyStatus_NotPaidToday_WhenLatestCollectionIsEarlier()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Lorna Buenades", "Lorna Buenades", new DateOnly(2026, 1, 1), 3, 900m);

        var today = PhilippineTime.Today;
        var earlier = new DateOnly(today.Year, today.Month, 1);
        // Ensure "earlier" is strictly before today; if today is the 1st, skip the assertion premise.
        if (earlier == today) earlier = today; // first-of-month edge: only one day available

        var dc = DailyCollection.Create(stall.Id, earlier);
        dc.MarkPaid("OR-EARLIER", Guid.NewGuid());

        context.AddRange(facility, stall, contract, dc);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var status = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);

        var row = Assert.Single(status);
        Assert.Equal(earlier == today, row.PaidToday); // false unless today is the 1st
        Assert.Equal(earlier, row.LastPaidDate);
        Assert.Equal(1, row.DaysPaidThisMonth);
    }
}
