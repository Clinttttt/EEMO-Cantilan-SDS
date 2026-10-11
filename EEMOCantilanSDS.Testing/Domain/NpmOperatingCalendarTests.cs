using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.NpmCalendar;

/// <summary>
/// The New Public Market's operating calendar: a recurring weekly closure that is prospective, explicit overrides that win over
/// it, and one effective answer per date. Pure rules, so every reader that uses them agrees by construction.
/// </summary>
public class NpmOperatingCalendarTests
{
    private static readonly DateOnly Policy = new(2026, 11, 2);   // a Monday

    private static NpmOperatingSchedule Weekends(DateOnly from) =>
        NpmOperatingSchedule.Create(from, [DayOfWeek.Saturday, DayOfWeek.Sunday], "Head memorandum", "head");

    private static NpmMarketClosure Closure(DateOnly date) => NpmMarketClosure.Create(date, MarketClosureReason.Holiday, "Holiday", "head");
    private static NpmMarketClosure Opened(DateOnly date) => NpmMarketClosure.CreateOpenOverride(date, "Working Saturday", "head");

    [Fact]
    public void WithNoScheduleEveryDayIsOpen_SoTodaysFiguresDoNotMove()
    {
        var closed = NpmOperatingCalendar.EffectiveClosedDates(new(2026, 11, 1), new(2026, 11, 30), [], []);
        Assert.Empty(closed);
    }

    [Fact]
    public void SaturdaysAndSundaysCloseFromTheEffectiveDate_AndWeekdaysStayOpen()
    {
        var closed = NpmOperatingCalendar.EffectiveClosedDates(new(2026, 11, 1), new(2026, 11, 30), [Weekends(Policy)], []);

        Assert.DoesNotContain(new DateOnly(2026, 11, 1), closed);          // the Sunday BEFORE the policy is not re-read
        Assert.Contains(new DateOnly(2026, 11, 7), closed);                // Saturday
        Assert.Contains(new DateOnly(2026, 11, 8), closed);                // Sunday
        Assert.DoesNotContain(new DateOnly(2026, 11, 9), closed);          // Monday
        Assert.Equal(8, closed.Count);                                     // 7, 8, 14, 15, 21, 22, 28, 29
    }

    [Fact]
    public void PastWeekendsBeforeThePolicyAreNeverClosedRetroactively()
    {
        var closed = NpmOperatingCalendar.EffectiveClosedDates(new(2026, 10, 1), new(2026, 10, 31), [Weekends(Policy)], []);
        Assert.Empty(closed);
    }

    [Fact]
    public void TheHeadCanOpenAWeekend_CloseAWeekday_AndBothOverridesWinOverTheSchedule()
    {
        var saturday = new DateOnly(2026, 11, 7);
        var wednesday = new DateOnly(2026, 11, 11);

        var closed = NpmOperatingCalendar.EffectiveClosedDates(new(2026, 11, 1), new(2026, 11, 30),
            [Weekends(Policy)], [Opened(saturday), Closure(wednesday)]);

        Assert.DoesNotContain(saturday, closed);
        Assert.Contains(wednesday, closed);
        Assert.Contains(new DateOnly(2026, 11, 8), closed);                // the Sunday is still closed
    }

    [Fact]
    public void StatesExplainWhyADayIsOpenOrClosed()
    {
        var schedules = new[] { Weekends(Policy) };
        Assert.Equal(NpmDayState.Open, NpmOperatingCalendar.StateOf(new(2026, 11, 9), schedules, null));
        Assert.Equal(NpmDayState.ClosedByWeeklySchedule, NpmOperatingCalendar.StateOf(new(2026, 11, 7), schedules, null));
        Assert.Equal(NpmDayState.OpenedByOverride, NpmOperatingCalendar.StateOf(new(2026, 11, 7), schedules, Opened(new(2026, 11, 7))));
        Assert.Equal(NpmDayState.ClosedByOverride, NpmOperatingCalendar.StateOf(new(2026, 11, 11), schedules, Closure(new(2026, 11, 11))));
        // An opening of a day that was never closed is meaningless and changes nothing.
        Assert.Equal(NpmDayState.Open, NpmOperatingCalendar.StateOf(new(2026, 11, 11), schedules, Opened(new(2026, 11, 11))));
    }

    [Fact]
    public void ALaterVersionReplacesAnEarlierOneOnlyFromItsOwnDate_SoHistoryIsNeverRewritten()
    {
        var saturdayOnly = NpmOperatingSchedule.Create(new(2026, 12, 1), [DayOfWeek.Saturday], "Revised", "head");
        var schedules = new[] { Weekends(Policy), saturdayOnly };

        Assert.True(NpmOperatingCalendar.ScheduledClosed(new(2026, 11, 29), schedules));    // a Sunday in November: still closed
        Assert.False(NpmOperatingCalendar.ScheduledClosed(new(2026, 12, 6), schedules));    // a Sunday after the revision: open
        Assert.True(NpmOperatingCalendar.ScheduledClosed(new(2026, 12, 5), schedules));     // a Saturday after it: closed
    }

    [Fact]
    public void AnEmptyVersionRestoresOpenEveryDayFromItsDate()
    {
        var reopen = NpmOperatingSchedule.Create(new(2026, 12, 1), [], null, "head");
        var schedules = new[] { Weekends(Policy), reopen };

        Assert.True(NpmOperatingCalendar.ScheduledClosed(new(2026, 11, 28), schedules));
        Assert.False(NpmOperatingCalendar.ScheduledClosed(new(2026, 12, 5), schedules));
    }

    [Theory]
    [InlineData("SuperAdmin", "2026-11-10", null, true)]
    [InlineData("Admin", "2026-11-10", null, true)]                          // today or later: the everyday act
    [InlineData("Admin", "2026-11-09", "Rain closed the market", false)]     // a past day is the Head's
    [InlineData("SuperAdmin", "2026-11-09", null, false)]                    // ...and needs a reason
    [InlineData("SuperAdmin", "2026-11-09", "ok", false)]
    [InlineData("SuperAdmin", "2026-11-09", "Flood closed the market", true)]
    [InlineData("Collector", "2026-11-10", null, false)]
    public void ChangingADayFollowsTheAuthorityRules(string role, string date, string? reason, bool allowed)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.Role).Returns(role);

        var refused = NpmOperatingDayAuthority.Check(user.Object, DateOnly.Parse(date), new DateOnly(2026, 11, 10), reason);

        Assert.Equal(allowed, refused is null);
    }
}
