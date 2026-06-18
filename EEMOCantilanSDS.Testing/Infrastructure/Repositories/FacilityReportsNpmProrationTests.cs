using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class FacilityReportsNpmProrationTests : RepositoryTestBase
{
    // Regression for the payment-modal "Monthly Rental" bug: an NPM payor whose contract starts
    // mid-month must be billed only for the days they actually occupy the stall (occupancy-prorated
    // daily fee), not the flat 30-day MonthlyRate. June 2026 has 30 days; a June-5 effectivity leaves
    // 26 collectable days → ExpectedBill = 26 × ₱30 = ₱780. With ₱240 collected (8 paid days), the
    // balance must be ₱540 — exactly what the modal should show, not 900 − 240 = 660.
    [Fact]
    public async Task MonthlyCompliance_MidMonthEffectivity_ProratesExpectedBillByOccupancyDays()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        // Payor started on the 5th of June 2026.
        var contract = Contract.Create(stall.Id, "Ana Reyes", "Ana Reyes", new DateOnly(2026, 6, 5), 3, 900m);

        context.AddRange(facility, stall, contract);

        // Eight paid collection days (June 5–12) = ₱240 collected.
        for (var day = 5; day <= 12; day++)
        {
            var daily = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, day));
            daily.MarkPaid($"OR-{day}", Guid.NewGuid());
            context.Add(daily);
        }

        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var compliance = Assert.Single(report.StallCompliance);
        Assert.Equal(780m, compliance.ExpectedBill);   // 26 occupancy days × ₱30 — NOT the flat ₱900
        Assert.Equal(240m, compliance.AmountPaid);      // 8 paid days × ₱30
        Assert.Equal(540m, compliance.Balance);         // 780 − 240, the value the modal must show
        Assert.Equal("Partial", compliance.Status);
    }

    // A full-month occupant (effective before the period) is billed for every day of the month:
    // June 2026 = 30 days × ₱30 = ₱900, matching the configured 30-day reference rate.
    [Fact]
    public async Task MonthlyCompliance_FullMonthOccupant_BillsEveryDay()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Full Month", "Full Month", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var compliance = Assert.Single(report.StallCompliance);
        Assert.Equal(900m, compliance.ExpectedBill);   // 30 days × ₱30
        Assert.Equal(0m, compliance.AmountPaid);
        Assert.Equal(900m, compliance.Balance);
        Assert.Equal("Unpaid", compliance.Status);
    }
}
