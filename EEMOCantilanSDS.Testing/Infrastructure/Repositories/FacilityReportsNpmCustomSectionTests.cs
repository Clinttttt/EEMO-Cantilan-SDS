using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Regression tests for per-LGU CUSTOM NPM sections (flat-daily). A custom-section stall has
/// Section == null and a CustomSectionName; it must surface as its own section-breakdown card and
/// reconcile to the facility total (mirrors the NCC custom-area behaviour). An NPM with no custom
/// sections (e.g. Cantilan) must be byte-for-byte unchanged — exactly the three canonical cards.
/// </summary>
public class FacilityReportsNpmCustomSectionTests : RepositoryTestBase
{
    [Fact]
    public async Task NpmSectionBreakdown_CustomSection_GetsItsOwnCard_AndReconcilesToFacilityTotal()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        // Canonical vegetable stall — one paid ₱30 day.
        var veg = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var vegContract = Contract.Create(veg.Id, "Veg Payor", "Veg Payor", new DateOnly(2026, 1, 1), 3, 900m);
        var vegDaily = DailyCollection.Create(veg.Id, new DateOnly(2026, 1, 10));
        vegDaily.MarkPaid("OR-V1", Guid.NewGuid());

        // Custom-section stall (Section null + CustomSectionName) — flat daily, one paid ₱30 day.
        var custom = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, customSectionName: "Sari-sari Area");
        var customContract = Contract.Create(custom.Id, "Custom Payor", "Custom Payor", new DateOnly(2026, 1, 1), 3, 900m);
        var customDaily = DailyCollection.Create(custom.Id, new DateOnly(2026, 1, 10));
        customDaily.MarkPaid("OR-C1", Guid.NewGuid());

        context.AddRange(facility, veg, vegContract, vegDaily, custom, customContract, customDaily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        // The custom section is its own card, carrying only its stall's revenue.
        var customCard = report.SectionBreakdown.Single(s => s.SectionName == "Sari-sari Area");
        Assert.Equal(30m, customCard.Revenue);
        Assert.Equal(1, customCard.TotalStalls);

        // The canonical cards still render (Vegetable holds the ₱30 veg day; Fish/Meat are empty ₱0 cards).
        Assert.Equal(30m, report.SectionBreakdown.Single(s => s.SectionName == "Vegetable Area").Revenue);
        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Fish Area");
        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Meat Area");

        // Reconciliation: the section cards (canonical + custom) sum to the facility's collected revenue —
        // the custom stall's money is never silently dropped.
        Assert.Equal(report.TotalRevenue, report.SectionBreakdown.Sum(s => s.Revenue));
        Assert.Equal(60m, report.TotalRevenue);
    }

    /// <summary>
    /// A section the office has closed leaves the report — unless it holds money for the period, which must still add up.
    /// </summary>
    /// <remarks>
    /// Reported from use 2026-09-06: Sari Sari was closed and still sat among the trading areas at ₱0, so the report showed
    /// the office a section it had just shut as though it were still open.
    ///
    /// <para>Both halves are asserted together on purpose. Simply filtering closed sections out would have been wrong: a
    /// section that took money earlier in the period still holds that money, and the section cards are required to sum to
    /// the facility's revenue - this file says so of itself. Dropping a card with ₱30 in it would lose revenue from the
    /// breakdown while the total kept it, which is a worse fault than the one being fixed.</para>
    /// </remarks>
    [Fact]
    public async Task NpmSectionBreakdown_AClosedSectionLeavesTheReport_UnlessItHoldsMoney()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        // Closed and EMPTY for the period — this is the card the office wants gone.
        var quiet = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, customSectionName: "Sari Sari");
        var quietContract = Contract.Create(quiet.Id, "Karmilita Log", "Karmilita Log", new DateOnly(2026, 1, 1), 3, 900m);

        // Closed but it TOOK ₱30 inside the period, so its card has to stay or the breakdown stops reconciling.
        var earner = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, customSectionName: "Kahoy Sale");
        var earnerContract = Contract.Create(earner.Id, "Pucci Lor", "Pucci Lor", new DateOnly(2026, 1, 1), 3, 900m);
        var earnerDaily = DailyCollection.Create(earner.Id, new DateOnly(2026, 1, 10));
        earnerDaily.MarkPaid("OR-K1", Guid.NewGuid());

        context.AddRange(facility, quiet, quietContract, earner, earnerContract, earnerDaily);
        context.FacilitySectionClosures.AddRange(
            FacilitySectionClosure.Create(FacilityCode.NPM, "Sari Sari", new DateOnly(2026, 1, 5), [quiet.Id], createdBy: "tester"),
            FacilitySectionClosure.Create(FacilityCode.NPM, "Kahoy Sale", new DateOnly(2026, 1, 20), [earner.Id], createdBy: "tester"));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        Assert.DoesNotContain(report.SectionBreakdown, s => s.SectionName == "Sari Sari");
        Assert.Equal(30m, report.SectionBreakdown.Single(s => s.SectionName == "Kahoy Sale").Revenue);

        // The reconciliation this file exists to protect: cards still sum to the facility's revenue.
        Assert.Equal(report.TotalRevenue, report.SectionBreakdown.Sum(s => s.Revenue));
    }

    [Fact]
    public async Task NpmSectionBreakdown_NoCustomSections_IsExactlyTheThreeCanonicalCards()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var veg = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var vegContract = Contract.Create(veg.Id, "Veg Payor", "Veg Payor", new DateOnly(2026, 1, 1), 3, 900m);
        var vegDaily = DailyCollection.Create(veg.Id, new DateOnly(2026, 1, 10));
        vegDaily.MarkPaid("OR-V1", Guid.NewGuid());

        context.AddRange(facility, veg, vegContract, vegDaily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        // Unchanged from before custom sections existed: exactly the three canonical cards, no extras.
        Assert.Equal(3, report.SectionBreakdown.Count);
        Assert.Equal(
            new[] { "Vegetable Area", "Fish Area", "Meat Area" }.OrderBy(x => x),
            report.SectionBreakdown.Select(s => s.SectionName).OrderBy(x => x));
        Assert.Equal(report.TotalRevenue, report.SectionBreakdown.Sum(s => s.Revenue));
    }

    [Fact]
    public async Task CustomSectionStall_IsCollectableAndNamed_InMobileNpmCollection()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var custom = Stall.Create(facility.Id, "9", 900m, ApplicableFees.DailyRental, dailyRate: 30m, customSectionName: "Sari-sari Area");
        var contract = Contract.Create(custom.Id, "Custom Payor", "Custom Payor", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, custom, contract);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);
        var dto = await repo.GetMobileNpmCollectionAsync(2026, 1, new DateOnly(2026, 1, 10), CancellationToken.None);

        // The custom-section stall appears in the collector's list (previously excluded by a Section.HasValue
        // filter) and shows its custom section name.
        var row = dto.Stalls.Single(s => s.StallNo == "9");
        Assert.Null(row.Section);
        Assert.Equal("Sari-sari Area", row.SectionName);
    }

    [Fact]
    public async Task CustomSection_Revenue_ReflectsItsOwnDailyRate()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        // Custom section priced at ₱50/day (not the ₱30 ordinance).
        var custom = Stall.Create(facility.Id, "1", 1500m, ApplicableFees.DailyRental, dailyRate: 50m, customSectionName: "Sari-sari Area");
        var contract = Contract.Create(custom.Id, "Payor", "Payor", new DateOnly(2026, 1, 1), 3, 1500m);
        // One paid day stamped at the custom ₱50 rate (as RecordDailyCollection would via Stall.ResolveDailyFee).
        var daily = DailyCollection.Create(custom.Id, new DateOnly(2026, 1, 10), "tester", 50m);
        daily.MarkPaid("OR-1", Guid.NewGuid());

        context.AddRange(facility, custom, contract, daily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        // The custom section's collected revenue is the ₱50 daily fee — not the ₱30 ordinance rate.
        var card = report.SectionBreakdown.Single(s => s.SectionName == "Sari-sari Area");
        Assert.Equal(50m, card.Revenue);
        Assert.Equal(report.TotalRevenue, report.SectionBreakdown.Sum(s => s.Revenue));
    }

    [Fact]
    public async Task CustomSection_MonthlyPaymentRecognition_UsesItsOwnDailyRate()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var custom = Stall.Create(facility.Id, "1", 1550m, ApplicableFees.DailyRental, dailyRate: 50m, customSectionName: "Sari-sari Area");
        var contract = Contract.Create(custom.Id, "Payor", "Payor", new DateOnly(2026, 1, 1), 3, 1550m);
        // A whole-month monthly payment (₱50 × 31 days) — recognized as daily fees at the CUSTOM ₱50 rate.
        var payment = PaymentRecord.Create(custom.Id, 2026, 1, 1550m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, custom, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        // Recognized at ₱50/day × 31 days = ₱1,550. At the ₱30 ordinance rate it would only recognize ₱930.
        var card = report.SectionBreakdown.Single(s => s.SectionName == "Sari-sari Area");
        Assert.Equal(1550m, card.Revenue);
    }
}
