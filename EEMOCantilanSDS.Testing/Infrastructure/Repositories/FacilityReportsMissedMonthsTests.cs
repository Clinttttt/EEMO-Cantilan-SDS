using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Locks the "Missed (mo)" delinquency/arrears count. Rule: a month counts only when it is fully in
/// the PAST and the stall was under an active contract that month — the current, in-progress month is
/// never counted (a payor is not in arrears for a month still underway), and future months are not due.
/// For NPM a past month is satisfied by a paid daily collection or any non-Unpaid monthly record.
/// Tests are today-relative so they stay deterministic whenever they run (they assume the current
/// month is at least April, which holds for the system's operating period).
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
    public async Task Npm_ContractStartedThisMonth_PartialPayment_HasZeroMissedMonths()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(today.Year, today.Month, 5), 3, 900m);
        var payment = PaymentRecord.Create(stall.Id, today.Year, today.Month, 900m);
        payment.UpdateStatus(PaymentStatus.Partial, 60m);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Pre-contract months are not counted; the current month is excluded → 0.
        Assert.Equal(0, await MissedMonthsFor(repo, FacilityCode.NPM, today.Year, today.Month, "1"));
    }

    [Fact]
    public async Task Npm_GenuineNonPayer_CountsOnlyElapsedPastMonths_ExcludingCurrent()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "No Pay", "No Pay", new DateOnly(today.Year, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Contract since January, never paid → Jan..(currentMonth-1) missed; current month excluded.
        Assert.Equal(today.Month - 1, await MissedMonthsFor(repo, FacilityCode.NPM, today.Year, today.Month, "1"));
    }

    [Fact]
    public async Task Npm_ContractStartedThisMonth_NoPayment_HasZeroMissedMonths()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Late Start", "Late Start", new DateOnly(today.Year, today.Month, 5), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Only the current month is collectable, and the current month is excluded → 0 (not 1).
        Assert.Equal(0, await MissedMonthsFor(repo, FacilityCode.NPM, today.Year, today.Month, "1"));
    }

    [Fact]
    public async Task Npm_PastMonthWithPaidDailyCollection_NotCounted()
    {
        var today = PhilippineTime.Today;
        var twoMonthsAgo = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Daily Payor", "Daily Payor", twoMonthsAgo, 3, 900m);
        // Paid a daily collection last month; nothing the month before that.
        var daily = DailyCollection.Create(stall.Id, lastMonth.AddDays(9));
        daily.MarkPaid("OR-1", Guid.NewGuid());

        context.AddRange(facility, stall, contract, daily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Two-months-ago = unpaid (missed); last month = covered by the daily collection; current excluded → 1.
        Assert.Equal(1, await MissedMonthsFor(repo, FacilityCode.NPM, today.Year, today.Month, "1"));
    }

    [Fact]
    public async Task Npm_PaidEarlyMonthsThenStops_CountsOnlyUnpaidElapsedMonths()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Stopped Paying", "Stopped Paying", new DateOnly(today.Year, 1, 1), 3, 900m);

        // Daily collections in Jan, Feb, Mar; nothing afterward.
        var collections = new[] { 1, 2, 3 }.Select(monthNo =>
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(today.Year, monthNo, 10));
            dc.MarkPaid($"OR-{monthNo}", Guid.NewGuid());
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, contract);
        context.AddRange(collections);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Jan–Mar covered; Apr..(currentMonth-1) unpaid; current excluded → (currentMonth-1) - 3.
        Assert.Equal(today.Month - 1 - 3, await MissedMonthsFor(repo, FacilityCode.NPM, today.Year, today.Month, "1"));
    }

    [Fact]
    public async Task NonNpm_UnpaidUnderContract_CountsElapsedPastMonths_ExcludingCurrent()
    {
        var today = PhilippineTime.Today;
        var twoMonthsAgo = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", twoMonthsAgo, 3, 1000m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Two elapsed past months under contract (two-months-ago and last month); current excluded → 2.
        Assert.Equal(2, await MissedMonthsFor(repo, FacilityCode.TCC, today.Year, today.Month, "101"));
    }

    [Fact]
    public async Task NonNpm_PaidMonthsAreNotMissed()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", new DateOnly(today.Year, 1, 1), 3, 1000m);
        var payment = PaymentRecord.Create(stall.Id, today.Year, today.Month, 1000m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        // Contract since January; the current month is paid (and excluded anyway); Jan..(currentMonth-1) unpaid.
        Assert.Equal(today.Month - 1, await MissedMonthsFor(repo, FacilityCode.TCC, today.Year, today.Month, "101"));
    }
}
