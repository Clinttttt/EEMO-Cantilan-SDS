using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Locks the corrected "Missed (mo)" delinquency count on the report page.
/// Before the fix it counted every calendar month lacking a fully-Paid monthly record, which
/// overstated NPM (daily-collected) stalls — a current partial payor read as "6 mo missed".
/// The corrected rule: only months the stall was under an active contract count, and for NPM a
/// month is satisfied by a paid daily collection or any non-Unpaid monthly record.
/// </summary>
public class FacilityReportsMissedMonthsTests : RepositoryTestBase
{
    private static async Task<int> MissedMonthsFor(
        FacilityReportsRepository repo, FacilityCode code, int year, int month, string stallNo)
    {
        var report = await repo.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, CancellationToken.None);
        return report.StallCompliance.Single(s => s.StallNo == stallNo).MissedMonths;
    }

    [Fact]
    public async Task Npm_PartialPayor_ContractStartedThisMonth_HasZeroMissedMonths()
    {
        // The real-world regression: contract effective Jun 5, a partial June payment.
        // Jan–May are pre-contract (not counted); June is partially paid (covered) → 0 missed.
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 6, 5), 3, 900m);
        var payment = PaymentRecord.Create(stall.Id, 2026, 6, 900m);
        payment.UpdateStatus(PaymentStatus.Partial, 60m);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(0, await MissedMonthsFor(repo, FacilityCode.NPM, 2026, 6, "1"));
    }

    [Fact]
    public async Task Npm_DailyCollectionOnly_NoMonthlyRecord_CountsMonthAsPaid()
    {
        // Contract effective Jun 1, paid one daily collection in June, no monthly record.
        // June is collectable and has a paid daily collection → covered → 0 missed.
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Daily Payor", "Daily Payor", new DateOnly(2026, 6, 1), 3, 900m);
        var daily = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 3));
        daily.MarkPaid("OR-1", Guid.NewGuid());

        context.AddRange(facility, stall, contract, daily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(0, await MissedMonthsFor(repo, FacilityCode.NPM, 2026, 6, "1"));
    }

    [Fact]
    public async Task Npm_GenuineNonPayer_CountsEveryCollectableMonth()
    {
        // Contract effective Jan 1, never paid anything by June → Jan–Jun all missed = 6.
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "No Pay", "No Pay", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(6, await MissedMonthsFor(repo, FacilityCode.NPM, 2026, 6, "1"));
    }

    [Fact]
    public async Task Npm_MidYearContract_DoesNotCountPreContractMonths()
    {
        // Contract effective Jun 5, never paid → only June is collectable and unpaid = 1 missed
        // (NOT 6 — the pre-contract Jan–May must not be counted).
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Late Start", "Late Start", new DateOnly(2026, 6, 5), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(1, await MissedMonthsFor(repo, FacilityCode.NPM, 2026, 6, "1"));
    }

    [Fact]
    public async Task Npm_PaidEarlyMonthsThenStops_CountsOnlyUnpaidCollectableMonths()
    {
        // Contract Jan 1; daily collections in Jan, Feb, Mar; nothing Apr–Jun → 3 missed.
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Stopped Paying", "Stopped Paying", new DateOnly(2026, 1, 1), 3, 900m);

        var collections = new[] { 1, 2, 3 }.Select(monthNo =>
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, monthNo, 10));
            dc.MarkPaid($"OR-{monthNo}", Guid.NewGuid());
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, contract);
        context.AddRange(collections);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(3, await MissedMonthsFor(repo, FacilityCode.NPM, 2026, 6, "1"));
    }

    [Fact]
    public async Task NonNpm_UnpaidUnderContract_CountsEveryCollectableMonth()
    {
        // TCC (monthly-billed) contract effective Apr 1, no payments → Apr, May, Jun = 3 missed.
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", new DateOnly(2026, 4, 1), 3, 1000m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(3, await MissedMonthsFor(repo, FacilityCode.TCC, 2026, 6, "101"));
    }

    [Fact]
    public async Task NonNpm_PaidMonthsAreNotMissed()
    {
        // TCC contract Jan 1; June paid, Jan–May unpaid → 5 missed (June covered).
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", new DateOnly(2026, 1, 1), 3, 1000m);
        var payment = PaymentRecord.Create(stall.Id, 2026, 6, 1000m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        Assert.Equal(5, await MissedMonthsFor(repo, FacilityCode.TCC, 2026, 6, "101"));
    }
}
