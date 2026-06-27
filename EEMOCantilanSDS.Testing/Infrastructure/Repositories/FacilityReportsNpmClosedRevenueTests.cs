using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Closing (freezing) a stall must never deduct money already collected. A closed NPM stall's prior
/// paid daily fees still count in the facility's collected revenue (recognized by contract, not by
/// current status), even though the stall is frozen out of the current obligation/pending.
/// </summary>
public class FacilityReportsNpmClosedRevenueTests : RepositoryTestBase
{
    [Fact]
    public async Task ClosedNpmStall_KeepsItsPaidDays_InCollectedRevenue_ButOwesNothing()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2024, 1, 1), 5, 900m);
        context.AddRange(facility, stall, contract);

        // Five paid days early in June 2026 (₱30 each), then the stall is closed mid-month.
        foreach (var day in new[] { 2, 3, 4, 5, 6 })
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            dc.MarkPaid($"OR-{day}", Guid.NewGuid());
            context.Add(dc);
        }
        stall.Close(new DateOnly(2026, 6, 10));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        // Collected money is preserved: 5 paid days × ₱30 = ₱150, even though the stall is now closed.
        Assert.Equal(150m, report.TotalRevenue);

        // ...and the section breakdown counts the same money (closed money is never dropped from a breakdown),
        // so it reconciles to the headline Collected total.
        Assert.Equal(150m, report.SectionBreakdown.Sum(s => s.Revenue));

        // ...and the frozen stall is not a current payor — no obligation/pending and no compliance row.
        Assert.Equal(0m, report.PendingPaymentAmount);
        Assert.Empty(report.StallCompliance);
    }
}
