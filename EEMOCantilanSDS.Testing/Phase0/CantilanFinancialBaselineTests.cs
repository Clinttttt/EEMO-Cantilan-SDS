using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Phase0;

/// <summary>
/// PHASE 0 — Golden baseline (characterization) test.
///
/// Unlike <c>GetFinancialReportQueryHandlerTests</c> (which mocks the repositories), this drives the
/// financial report through the REAL repositories over a real in-memory <see cref="Infrastructure.Persistence.AppDbContext"/>,
/// against a fixed, hand-computed dataset. That makes it a byte-for-byte tripwire for the layer where
/// the CARCANMADCARLAN multi-LGU work lands:
///   • Phase 3 (add MunicipalityId + EF global query filters) must not change these totals.
///   • Phase 4 (move rates/facilities to per-LGU config) must not change these totals.
/// If any of these assertions change, a refactor altered Cantilan's money — investigate before merging.
///
/// Fixed dataset (period June 2024 — a fully-elapsed month, so no "current month" ambiguity):
///   TCC (monthly rental, ₱2,400):
///     • Stall 101 — fully Paid  → collected 2,400, balance 0
///     • Stall 102 — no record   → owes the month → balance 2,400 (Unpaid)
///   SLH (paid-on-service): one hog slaughter (₱250) on 2024-06-10.
///   NPM / NCC / BBQ / ICE: facility rows exist but hold no stalls → contribute 0.
///
/// Expected composed totals:
///   collected = 2,400 (TCC) + 250 (SLH)                     = 2,650
///   unpaid    = 2,400 (TCC; SLH is paid-on-service)         = 2,400
///   billed    = collected + unpaid                          = 5,050
///   rate      = round(2,650 / 5,050 × 100) = round(52.47)   = 52
/// </summary>
public class CantilanFinancialBaselineTests : RepositoryTestBase
{
    private const int Year = 2024;
    private const int Month = 6;

    [Fact]
    public async Task FinancialReport_AllFacilities_MatchesGoldenBaseline()
    {
        var context = NewContext();

        // All eight facility rows must exist — the financial handler now lists only the CURRENT tenant's
        // facilities (via IFacilityRepository), and Cantilan operates all eight. Seeding the three
        // paid-on-service rows (SLH/TRM/TPM) keeps the baseline at 8 facilities; they hold no stalls and
        // (except the one SLH transaction below) no activity, so every money assertion is unchanged.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var ncc = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var bbq = Facility.Create(FacilityCode.BBQ, "Barbecue Stand", "BBQ");
        var ice = Facility.Create(FacilityCode.ICE, "Iceplant", "ICE");
        var slh = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH");
        var trm = Facility.Create(FacilityCode.TRM, "Transport Terminal", "TRM");
        var tpm = Facility.Create(FacilityCode.TPM, "Tabo-an Public Market", "TPM");

        // TCC — monthly rental (₱2,400): one fully paid, one unpaid (no record).
        var paidStall = Stall.Create(tcc.Id, "101", 2_400m, ApplicableFees.BaseRental);
        var unpaidStall = Stall.Create(tcc.Id, "102", 2_400m, ApplicableFees.BaseRental);
        var paidContract = Contract.Create(paidStall.Id, "Ligaya Ramos", "Ligaya Ramos", new DateOnly(2024, 1, 1), 5, 2_400m);
        var unpaidContract = Contract.Create(unpaidStall.Id, "Mario Bautista", "Mario Bautista", new DateOnly(2024, 1, 1), 5, 2_400m);
        var junePaid = PaymentRecord.Create(paidStall.Id, Year, Month, 2_400m);
        junePaid.UpdateStatus(PaymentStatus.Paid);

        // SLH — paid on service: one hog (₱250).
        var hog = SlaughterTransaction.CreateHog(bbq.Id /* facilityId unused by month query */, null, "Pedro Cruz", 1, "OR-SLH-1", new DateOnly(Year, Month, 10));

        context.AddRange(npm, tcc, ncc, bbq, ice, slh, trm, tpm,
            paidStall, unpaidStall, paidContract, unpaidContract, junePaid, hog);
        await context.SaveChangesAsync();

        var handler = new GetFinancialReportQueryHandler(
            new FacilityReportsRepository(context),
            new SlaughterRepository(context),
            new TrmRepository(context),
            new TpmRepository(context, CacheTestDoubles.TpmMarketDay),
            new TransactionFeedRepository(context),
            new FacilityRepository(context),
            CacheTestDoubles.FeeRateResolver,
            CacheTestDoubles.PassthroughCache,
            CacheTestDoubles.Tenant,
            new EemoCacheOptions());

        var result = await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, Year, Month, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        // ── Headline golden totals ──
        Assert.Equal(2_650m, r.Collected);
        Assert.Equal(2_400m, r.CurrentPeriodUnpaid);
        Assert.Equal(5_050m, r.Billed);
        Assert.Equal(52, r.CollectionRatePct);
        Assert.Equal(8, r.FacilityCount);

        // Facility rows reconcile to the headline totals.
        Assert.Equal(r.Collected, r.Facilities.Sum(f => f.Collected));
        Assert.Equal(r.CurrentPeriodUnpaid, r.Facilities.Where(f => f.Unpaid.HasValue).Sum(f => f.Unpaid!.Value));

        // ── TCC (monthly rental) ──
        var tccRow = r.Facilities.Single(f => f.Code == FacilityCode.TCC);
        Assert.False(tccRow.PaidOnService);
        Assert.Equal(2_400m, tccRow.Collected);
        Assert.Equal(2_400m, tccRow.Unpaid);

        // ── SLH (paid on service) ──
        var slhRow = r.Facilities.Single(f => f.Code == FacilityCode.SLH);
        Assert.True(slhRow.PaidOnService);
        Assert.Equal(250m, slhRow.Collected);
        Assert.Null(slhRow.Unpaid);

        // ── Empty stall facilities contribute nothing ──
        Assert.Equal(0m, r.Facilities.Single(f => f.Code == FacilityCode.NPM).Collected);
        Assert.Equal(0m, r.Facilities.Single(f => f.Code == FacilityCode.NCC).Collected);
    }
}
