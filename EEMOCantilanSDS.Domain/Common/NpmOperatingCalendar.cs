using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Domain.Common
{
    /// <summary>What the New Public Market's operating calendar says about one day, and why.</summary>
    public enum NpmDayState
    {
        /// <summary>The market opens and the day is billable as usual.</summary>
        Open = 1,

        /// <summary>The recurring weekly schedule closes this weekday and nothing overrides it.</summary>
        ClosedByWeeklySchedule = 2,

        /// <summary>An explicit closure closes this day (a holiday, weather, maintenance …).</summary>
        ClosedByOverride = 3,

        /// <summary>The schedule would close this day but the Head explicitly opened it.</summary>
        OpenedByOverride = 4
    }

    /// <summary>
    /// The single answer to "is the New Public Market open on this date?". Every reader — Collection Manager, the Mobile
    /// collector, the NPM ledger, settlement, arrears and the reports — takes its closed dates from here, so no two views can
    /// disagree about a day. Pure: it reads nothing and writes nothing.
    /// </summary>
    /// <remarks>
    /// Precedence, highest first: an explicit closure row closes the day; an explicit open-override row opens a day the
    /// schedule would close; otherwise the weekly schedule in force on that date decides; with no schedule in force the day is
    /// open. A schedule applies only from its own effective date, so a past date is never re-read under a rule adopted later.
    /// </remarks>
    public static class NpmOperatingCalendar
    {
        /// <summary>The schedule in force on a date: the latest version whose effective date has been reached, or none.</summary>
        public static NpmOperatingSchedule? InForce(DateOnly date, IEnumerable<NpmOperatingSchedule> schedules) =>
            schedules.Where(s => s.EffectiveFrom <= date).OrderByDescending(s => s.EffectiveFrom).FirstOrDefault();

        public static bool ScheduledClosed(DateOnly date, IEnumerable<NpmOperatingSchedule> schedules) =>
            InForce(date, schedules) is { } schedule && schedule.IsClosedOn(date.DayOfWeek);

        public static NpmDayState StateOf(DateOnly date, IEnumerable<NpmOperatingSchedule> schedules, NpmMarketClosure? explicitRow)
        {
            var scheduledClosed = ScheduledClosed(date, schedules);
            if (explicitRow is { IsOpenOverride: false }) return NpmDayState.ClosedByOverride;
            if (explicitRow is { IsOpenOverride: true }) return scheduledClosed ? NpmDayState.OpenedByOverride : NpmDayState.Open;
            return scheduledClosed ? NpmDayState.ClosedByWeeklySchedule : NpmDayState.Open;
        }

        public static bool IsClosed(NpmDayState state) => state is NpmDayState.ClosedByWeeklySchedule or NpmDayState.ClosedByOverride;

        /// <summary>The effective closed dates between two dates, both included.</summary>
        public static HashSet<DateOnly> EffectiveClosedDates(
            DateOnly from, DateOnly to, IReadOnlyList<NpmOperatingSchedule> schedules, IEnumerable<NpmMarketClosure> explicitRows)
        {
            var byDate = explicitRows.GroupBy(r => r.ClosureDate).ToDictionary(g => g.Key, g => g.First());
            var closed = new HashSet<DateOnly>();
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                byDate.TryGetValue(day, out var row);
                if (IsClosed(StateOf(day, schedules, row))) closed.Add(day);
            }
            return closed;
        }
    }
}
