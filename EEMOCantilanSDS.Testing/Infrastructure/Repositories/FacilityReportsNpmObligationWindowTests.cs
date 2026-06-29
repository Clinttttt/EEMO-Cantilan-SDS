using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// CHARACTERIZATION (snapshot) tests — they capture the report engine's CURRENT obligation-window
/// behavior, with NO behavior change. They exist to make the upcoming "clamp the current-period
/// obligation to today" fix safe: exactly one of them is expected to change (the in-progress month),
/// while the past-month guard must stay green.
///
/// Background: the Collection Manager computes a payor's NPM obligation up to TODAY (future days are
/// "future", excused), e.g. ₱690. The financial/Month-End report computes it over the whole month
/// (compliance window endDate = month-end, NOT clamped to today), so a mid-month run counts days that
/// have not yet elapsed as already owed — the source of the "why 690 vs the report" mismatch.
/// </summary>
public class FacilityReportsNpmObligationWindowTests : RepositoryTestBase
{
    private static (Facility f, Stall s, Contract c) NewNpmStall(DateOnly contractStart)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Window Test", "Window Test", contractStart, 5, 900m);
        return (facility, stall, contract);
    }

    [Fact]
    public async Task CurrentMonth_Obligation_CountsThroughMonthEnd_IncludingFutureDays()
    {
        // CURRENT behavior: for the in-progress current month, the report's NPM obligation counts EVERY
        // collectable day through MONTH-END (incl. days that haven't elapsed), because the compliance
        // window's endDate is the last day of the month and is not clamped to today.
        // → ExpectedBill = (full days in month) × ₱30, even though only `today.Day` days have elapsed.
        // When the obligation window is later clamped to today, THIS is the number that should change
        // (to `today.Day × ₱30`, minus any excused).
        var today = PhilippineTime.Today;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var context = NewContext();
        var (facility, stall, contract) = NewNpmStall(new DateOnly(today.Year, today.Month, 1));
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, today.Year, today.Month, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(daysInMonth * FeeRates.NpmDailyFee, c.ExpectedBill);   // full month, incl. future days
        Assert.Equal(0m, c.AmountPaid);
        Assert.Equal(daysInMonth * FeeRates.NpmDailyFee, c.Balance);
    }

    [Fact]
    public async Task PastMonth_Obligation_IsFullMonth_AndMustStayStableAfterAnyTodayClamp()
    {
        // EQUIVALENCE GUARD: a fully-elapsed PAST month already equals the full month, so clamping the
        // obligation window to today must NOT change this value (min(monthEnd, today) == monthEnd for a
        // past month). This test should stay green through the upcoming fix.
        var anchor = PhilippineTime.Today.AddMonths(-2);
        var daysInMonth = DateTime.DaysInMonth(anchor.Year, anchor.Month);

        var context = NewContext();
        var (facility, stall, contract) = NewNpmStall(new DateOnly(anchor.Year - 1, 1, 1));
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, anchor.Year, anchor.Month, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(daysInMonth * FeeRates.NpmDailyFee, c.ExpectedBill);
    }
}
