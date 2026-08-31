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
    public async Task CurrentMonth_Obligation_StopsAtToday_NotAtMonthEnd()
    {
        // The number this file was written to watch change. The report's NPM obligation for the month in progress
        // counts only the collectable days that have ELAPSED: a market space is charged per market day, so a day
        // still to come has not been earned. Before this the reports billed the whole month while the stall
        // profile's ledger billed the days so far, so one stall carried two balances.
        var today = PhilippineTime.Today;
        // Capped by the month's rent: a month held in full owes the rent and never more, so on the 31st of a 31-day
        // month this is ₱900 rather than ₱930. fee × day alone would pass most days and fail at a month end.
        var elapsedCharge = DomainRules.DailyBilledMonthObligation(
            FeeRates.NpmDailyFee, 0m, DateTime.DaysInMonth(today.Year, today.Month), today.Day);

        var context = NewContext();
        var (facility, stall, contract) = NewNpmStall(new DateOnly(today.Year, today.Month, 1));
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, today.Year, today.Month, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(elapsedCharge, c.ExpectedBill);                        // the days earned, not the month
        Assert.True(c.ExpectedBill <= FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays);
        Assert.Equal(0m, c.AmountPaid);
        Assert.Equal(elapsedCharge, c.Balance);
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
        // A month held in full owes the month's RENT, whatever the calendar gave it. Asked of the rule rather than
        // multiplied out, because multiplying passed only while the month under test had thirty days.
        Assert.Equal(
            DomainRules.DailyBilledMonthObligation(FeeRates.NpmDailyFee, 0m, daysInMonth, daysInMonth),
            c.ExpectedBill);
    }
}
