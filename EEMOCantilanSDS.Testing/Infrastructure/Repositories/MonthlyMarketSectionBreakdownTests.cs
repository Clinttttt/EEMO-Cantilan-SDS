using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A market rented by the month reports its monthly stall rental per section, read from the monthly records exactly as the
/// facility totals are, so the section cards reconcile to the headline revenue. A daily market keeps its own breakdown.
/// </summary>
public class MonthlyMarketSectionBreakdownTests : RepositoryTestBase
{
    [Fact]
    public async Task AMonthlyMarket_ReportsRentPerSection_AndTheCardsReconcileToTheFacilityRevenue()
    {
        var ctx = NewContext();
        var market = Facility.Create(FacilityCode.NPM, "Madrid Public Market", "MPM", archetype: BillingArchetype.MonthlyRental);
        var veg = Stall.Create(market.Id, "1", 1200m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);
        var meat = Stall.Create(market.Id, "2", 1500m, ApplicableFees.BaseRental, section: MarketSection.MeatSection);
        var none = Stall.Create(market.Id, "3", 1000m, ApplicableFees.BaseRental);
        var start = new DateOnly(2026, 1, 1);
        ctx.AddRange(market, veg, meat, none,
            Contract.Create(veg.Id, "A", "A", start, 3, 1200m),
            Contract.Create(meat.Id, "B", "B", start, 3, 1500m),
            Contract.Create(none.Id, "C", "C", start, 3, 1000m));

        var paid = PaymentRecord.Create(veg.Id, 2026, 6, 1200m);
        paid.UpdateStatus(PaymentStatus.Paid, 0m);
        var part = PaymentRecord.Create(meat.Id, 2026, 6, 1500m);
        part.UpdateStatus(PaymentStatus.Partial, 500m);
        ctx.AddRange(paid, part);
        await ctx.SaveChangesAsync();

        var report = await new FacilityReportsRepository(ctx).GetFacilityReportsAsync(
            FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.False(report.BillsDaily);
        Assert.Equal(["Vegetable Area", "Meat Area", "No Section"], report.SectionBreakdown.Select(s => s.SectionName).ToArray());
        Assert.Equal(1200m, report.SectionBreakdown[0].Revenue);
        Assert.Equal(500m, report.SectionBreakdown[1].Revenue);
        Assert.Equal(0m, report.SectionBreakdown[2].Revenue);
        Assert.Equal(report.TotalRevenue, report.SectionBreakdown.Sum(s => s.Revenue));
    }

    [Fact]
    public async Task AFacilityOtherThanTheMarketOrNcc_StillHasNoSectionBreakdown()
    {
        var ctx = NewContext();
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "1", 900m, ApplicableFees.BaseRental);
        ctx.AddRange(tcc, stall, Contract.Create(stall.Id, "A", "A", new DateOnly(2026, 1, 1), 3, 900m));
        await ctx.SaveChangesAsync();

        var report = await new FacilityReportsRepository(ctx).GetFacilityReportsAsync(
            FacilityCode.TCC, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Empty(report.SectionBreakdown);
    }
}
