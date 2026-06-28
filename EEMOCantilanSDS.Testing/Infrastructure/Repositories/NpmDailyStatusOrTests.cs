using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The NPM daily status must report the OR of the SINGLE most-recent paid day (LastPaidORNumber) —
/// blank when that day was collected without one — so the admin card never shows an older day's OR
/// as if it were today's, and surfaces the "awaiting OR" prompt for the latest day.
/// </summary>
public class NpmDailyStatusOrTests : RepositoryTestBase
{
    [Fact]
    public async Task LastPaidORNumber_IsLatestPaidDaysOr_BlankWhenLatestDayHasNone()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var earlierDay = new DateOnly(today.Year, today.Month, 1);
        var latestDay = new DateOnly(today.Year, today.Month, 2);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);

        var earlier = DailyCollection.Create(stall.Id, earlierDay);
        earlier.MarkPaid("OR-A", Guid.NewGuid());          // earlier day HAS an OR
        var latest = DailyCollection.Create(stall.Id, latestDay);
        latest.MarkPaid(string.Empty, collectorId: null);  // latest day paid WITHOUT an OR

        context.AddRange(facility, stall, earlier, latest);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var statuses = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);
        var status = Assert.Single(statuses);

        Assert.Equal(latestDay, status.LastPaidDate);                 // most-recent paid day
        Assert.Equal("OR-A", status.LastORNumber);                    // most-recent NON-blank OR (earlier day)
        Assert.True(string.IsNullOrEmpty(status.LastPaidORNumber));   // latest day has none → awaiting OR
    }

    [Fact]
    public async Task LastPaidORNumber_IsSet_WhenLatestPaidDayHasOr()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var day = new DateOnly(today.Year, today.Month, 3);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var paid = DailyCollection.Create(stall.Id, day);
        paid.MarkPaid("OR-Z", Guid.NewGuid());

        context.AddRange(facility, stall, paid);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var statuses = await repo.GetNpmDailyStatusAsync(FacilityCode.NPM, today.Year, today.Month, CancellationToken.None);
        var status = Assert.Single(statuses);

        Assert.Equal("OR-Z", status.LastPaidORNumber);   // latest paid day's OR present → button hidden
    }
}
