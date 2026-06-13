using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Regression tests for the NCC per-area (Corner / Extension / Standard) section breakdown.
/// </summary>
public class FacilityReportsNccSectionBreakdownTests : RepositoryTestBase
{
    // Regression (#1): the Yearly view must scale the expected obligation across every month in
    // the period. Previously expected was a single month's rent, so ~12 months of collections
    // divided by one month read as collection rates far above 100% (e.g. 300%).
    [Fact]
    public async Task NccYearlySectionBreakdown_ScalesExpectedAcrossMonths_RateNeverExceeds100()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var stall = Stall.Create(facility.Id, "1", 3000m, ApplicableFees.BaseRental, areaLocation: NccAreaLocation.Corner);
        var contract = Contract.Create(stall.Id, "Corner Payor", "Corner Payor", new DateOnly(2024, 1, 1), 3, 3000m);

        var jan = PaymentRecord.Create(stall.Id, 2024, 1, 3000m); jan.UpdateStatus(PaymentStatus.Paid);
        var feb = PaymentRecord.Create(stall.Id, 2024, 2, 3000m); feb.UpdateStatus(PaymentStatus.Paid);
        var mar = PaymentRecord.Create(stall.Id, 2024, 3, 3000m); mar.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, jan, feb, mar);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NCC, ReportPeriod.Yearly, 2024, null, null, CancellationToken.None);

        var corner = report.SectionBreakdown.Single(s => s.SectionName == "Corner");
        Assert.Equal(9000m, corner.Revenue);
        // ₱9,000 collected / (₱3,000 × 12 due months in the past year) = 25% — not 300% (one-month baseline bug).
        Assert.Equal(25m, corner.Percentage);
        Assert.True(corner.Percentage <= 100m);
    }

    // Regression (#2): Standard-area stalls were dropped entirely; empty tiers should not appear.
    [Fact]
    public async Task NccSectionBreakdown_IncludesStandardArea_AndOmitsEmptyTiers()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var corner = Stall.Create(facility.Id, "1", 3000m, ApplicableFees.BaseRental, areaLocation: NccAreaLocation.Corner);
        var standard = Stall.Create(facility.Id, "2", 1200m, ApplicableFees.BaseRental, areaLocation: NccAreaLocation.Standard);
        var cornerContract = Contract.Create(corner.Id, "Corner Payor", "Corner Payor", new DateOnly(2026, 1, 1), 3, 3000m);
        var standardContract = Contract.Create(standard.Id, "Standard Payor", "Standard Payor", new DateOnly(2026, 1, 1), 3, 1200m);

        context.AddRange(facility, corner, standard, cornerContract, standardContract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NCC, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Corner");
        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Standard");      // previously missing
        Assert.DoesNotContain(report.SectionBreakdown, s => s.SectionName == "Extension"); // no empty placeholder
        Assert.Equal(2, report.SectionBreakdown.Count);
    }

    // Stalls with no area tier set still bill rent; they must surface as a "No Location" card so the
    // area breakdown reconciles with the facility total instead of silently dropping their revenue.
    [Fact]
    public async Task NccSectionBreakdown_SurfacesStallsWithNoAreaLocation_AsNoLocationCard()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var corner = Stall.Create(facility.Id, "1", 3000m, ApplicableFees.BaseRental, areaLocation: NccAreaLocation.Corner);
        var noLoc = Stall.Create(facility.Id, "2", 1200m, ApplicableFees.BaseRental); // no area location
        var cornerContract = Contract.Create(corner.Id, "Corner Payor", "Corner Payor", new DateOnly(2026, 1, 1), 3, 3000m);
        var noLocContract = Contract.Create(noLoc.Id, "Unzoned Payor", "Unzoned Payor", new DateOnly(2026, 1, 1), 3, 1200m);

        var cornerPay = PaymentRecord.Create(corner.Id, 2026, 1, 3000m); cornerPay.UpdateStatus(PaymentStatus.Paid);
        var noLocPay = PaymentRecord.Create(noLoc.Id, 2026, 1, 1200m); noLocPay.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, corner, noLoc, cornerContract, noLocContract, cornerPay, noLocPay);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NCC, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        var noLocation = report.SectionBreakdown.Single(s => s.SectionName == "No Location");
        Assert.Equal(1200m, noLocation.Revenue);
        Assert.Equal(1, noLocation.TotalStalls);
        // The area cards now sum to the facility's collected revenue (no silently-dropped stalls).
        Assert.Equal(report.TotalRevenue, report.SectionBreakdown.Sum(s => s.Revenue));
    }

    // Regression (#7): stall numbers must sort naturally ("2" before "10"), not lexicographically.
    [Fact]
    public async Task StallCompliance_OrdersStallNumbersNaturally()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        context.Add(facility);
        var effectivity = new DateOnly(2026, 1, 1);
        // Add out of order, including a two-digit number that lexicographic sort would mis-place.
        foreach (var no in new[] { "10", "2", "1", "3" })
        {
            var stall = Stall.Create(facility.Id, no, 1200m, ApplicableFees.BaseRental, areaLocation: NccAreaLocation.Corner);
            context.Add(stall);
            context.Add(Contract.Create(stall.Id, $"Payor {no}", $"Payor {no}", effectivity, 3, 1200m));
        }
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NCC, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        Assert.Equal(new[] { "1", "2", "3", "10" }, report.StallCompliance.Select(s => s.StallNo).ToArray());
    }
}
