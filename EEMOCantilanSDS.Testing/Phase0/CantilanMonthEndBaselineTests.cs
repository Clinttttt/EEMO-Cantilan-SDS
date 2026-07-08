using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Queries.Facilities.GetMonthEndReport;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Phase0;

/// <summary>
/// PHASE 0 — Golden baseline (characterization) test for the Month-End report.
///
/// The existing <c>GetMonthEndReportQueryHandlerTests</c> mocks the repositories; this drives the
/// report through the REAL repositories over a real in-memory <see cref="Infrastructure.Persistence.AppDbContext"/>
/// against the same fixed June-2024 dataset used by the financial baseline, so the two reports are
/// proven to reconcile against one source of truth. It is a byte-for-byte tripwire for Phases 3–4.
///
/// Dataset: TCC stall 101 fully Paid (₱2,400); TCC stall 102 unpaid (owes ₱2,400); one SLH hog (₱250).
/// Expected: TotalCollected 2,650 · TotalOutstanding 2,400 · OverallCollectionRate 52 · "June 2024".
/// </summary>
public class CantilanMonthEndBaselineTests : RepositoryTestBase
{
    private const int Year = 2024;
    private const int Month = 6;

    [Fact]
    public async Task MonthEndReport_MatchesGoldenBaseline_AndReconcilesToFinancial()
    {
        var context = NewContext();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var ncc = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var bbq = Facility.Create(FacilityCode.BBQ, "Barbecue Stand", "BBQ");
        var ice = Facility.Create(FacilityCode.ICE, "Iceplant", "ICE");
        // Cantilan operates all eight; the report now lists only the tenant's facilities, so all eight
        // rows (incl. the paid-on-service SLH/TRM/TPM) must be seeded to keep the golden at 8 facilities.
        var slh = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH");
        var trm = Facility.Create(FacilityCode.TRM, "Transport Terminal", "TRM");
        var tpm = Facility.Create(FacilityCode.TPM, "Tabo-an Public Market", "TPM");

        var paidStall = Stall.Create(tcc.Id, "101", 2_400m, ApplicableFees.BaseRental);
        var unpaidStall = Stall.Create(tcc.Id, "102", 2_400m, ApplicableFees.BaseRental);
        var paidContract = Contract.Create(paidStall.Id, "Ligaya Ramos", "Ligaya Ramos", new DateOnly(2024, 1, 1), 5, 2_400m);
        var unpaidContract = Contract.Create(unpaidStall.Id, "Mario Bautista", "Mario Bautista", new DateOnly(2024, 1, 1), 5, 2_400m);
        var junePaid = PaymentRecord.Create(paidStall.Id, Year, Month, 2_400m);
        junePaid.UpdateStatus(PaymentStatus.Paid);

        var hog = SlaughterTransaction.CreateHog(bbq.Id, null, "Pedro Cruz", 1, "OR-SLH-1", new DateOnly(Year, Month, 10));

        context.AddRange(npm, tcc, ncc, bbq, ice, slh, trm, tpm,
            paidStall, unpaidStall, paidContract, unpaidContract, junePaid, hog);
        await context.SaveChangesAsync();

        var handler = new GetMonthEndReportQueryHandler(
            new FacilityReportsRepository(context),
            new SlaughterRepository(context),
            new TrmRepository(context),
            new TpmRepository(context, CacheTestDoubles.TpmMarketDay),
            new FacilityRepository(context),
            CacheTestDoubles.FeeRateResolver,
            CacheTestDoubles.PassthroughCache,
            CacheTestDoubles.Tenant,
            new EemoCacheOptions());

        var result = await handler.Handle(new GetMonthEndReportQuery(Year, Month), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        // ── Grand totals (golden) ──
        Assert.Equal(2_650m, r.TotalCollected);
        Assert.Equal(2_400m, r.TotalOutstanding);
        Assert.Equal(52, r.OverallCollectionRate);
        Assert.Equal("June 2024", r.PeriodLabel);

        // All eight facilities present, in enum order.
        Assert.Equal(
            new[] { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE, FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM },
            r.Facilities.Select(f => f.Code).ToArray());

        // Grand totals reconcile to the per-facility figures.
        Assert.Equal(r.Facilities.Sum(f => f.Collected), r.TotalCollected);
        Assert.Equal(r.Facilities.Sum(f => f.Outstanding), r.TotalOutstanding);

        // ── TCC (rental, per-payor) ──
        var tccRow = r.Facilities.Single(f => f.Code == FacilityCode.TCC);
        Assert.True(tccRow.IsRental);
        Assert.Equal(2_400m, tccRow.Collected);
        Assert.Equal(2_400m, tccRow.Outstanding);
        Assert.Equal(2, tccRow.Payors.Count);

        // ── SLH (transaction, grouped by payor) ──
        var slhRow = r.Facilities.Single(f => f.Code == FacilityCode.SLH);
        Assert.False(slhRow.IsRental);
        Assert.Equal(250m, slhRow.Collected);
        var pedro = Assert.Single(slhRow.TransactionPayors);
        Assert.Equal("Pedro Cruz", pedro.Payor);
        Assert.Equal(250m, pedro.TotalCollected);
        Assert.Equal(1, pedro.Quantity);
        Assert.Equal("1 Hog", pedro.Summary);
    }
}
