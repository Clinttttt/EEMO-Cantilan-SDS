using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The boundary rule for a daily-billed obligation: no day beyond the as-of date has been earned.
/// <para>
/// A market space is charged per market day, so a report run on the sixth of August cannot state that September is
/// owed, nor that the whole of August is. Six paths in the system compute such an obligation and they disagreed about
/// the month in progress — the stall profile stated the days earned, the reports and the collector's own report
/// stated the whole month — so one stall carried two balances depending on which screen the office opened. The rule
/// is stated once, here, so the paths cannot drift again.
/// </para>
/// </summary>
public class EarnedThroughTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 6);

    [Fact]
    public void AMonthThatHasClosed_KeepsItsWholeWindow()
    {
        // July ended before the as-of date, so all of it was earned and its obligation must not move.
        var julyEnd = new DateOnly(2026, 7, 31);
        Assert.Equal(julyEnd, DomainRules.EarnedThrough(julyEnd, AsOf));
    }

    [Fact]
    public void TheMonthInProgress_StopsAtTheAsOfDate()
    {
        // August is not over: the days from the seventh onward have not been earned and are not owed.
        Assert.Equal(AsOf, DomainRules.EarnedThrough(new DateOnly(2026, 8, 31), AsOf));
    }

    [Fact]
    public void AMonthEntirelyInTheFuture_YieldsAWindowThatCannotBeBilled()
    {
        // September's window ends before it starts once clamped, which every caller reads as nothing owed. This is
        // what keeps a year-to-date report from stating a projected January-to-December receivable.
        var septemberStart = new DateOnly(2026, 9, 1);
        var earned = DomainRules.EarnedThrough(new DateOnly(2026, 9, 30), AsOf);

        Assert.True(earned < septemberStart, $"a future month must not be billable; got {earned:yyyy-MM-dd}");
    }

    [Fact]
    public void AnOccupancyThatEndedEarlier_KeepsItsOwnLastDay()
    {
        // A term that lapsed on 7 June is bounded by its own end, not by today: the clamp only ever removes days
        // that have not happened, never days the space was not held.
        var lapsedOn = new DateOnly(2026, 6, 7);
        Assert.Equal(lapsedOn, DomainRules.EarnedThrough(lapsedOn, AsOf));
    }

    [Fact]
    public void OnTheAsOfDateItself_TheDayIsEarned()
    {
        // The office collects on the day it is standing in, so the boundary is inclusive.
        Assert.Equal(AsOf, DomainRules.EarnedThrough(AsOf, AsOf));
    }

    [Fact]
    public void AClosedPeriodIsStatedAsOfItsOwnEnd_NotToday()
    {
        // A report for a period that has closed passes that period's end as the as-of date, so it states what was
        // earned then — a 2025 report does not grow because 2026 has arrived.
        var periodEnd = new DateOnly(2025, 12, 31);
        Assert.Equal(periodEnd, DomainRules.EarnedThrough(new DateOnly(2025, 12, 31), periodEnd));
        Assert.Equal(new DateOnly(2025, 8, 31), DomainRules.EarnedThrough(new DateOnly(2025, 8, 31), periodEnd));
    }
}
