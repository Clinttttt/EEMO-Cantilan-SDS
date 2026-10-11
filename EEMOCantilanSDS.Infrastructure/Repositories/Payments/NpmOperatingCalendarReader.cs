using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// The one database reader for "which New Public Market days are closed". Every reader that used to select closure rows
/// directly — settlement, arrears, closed accounts, the ledger, the reports and the Mobile collector — goes through here, so a
/// recurring weekly closure, an explicit closure and an explicit opening are resolved identically everywhere.
/// </summary>
public static class NpmOperatingCalendarReader
{
    /// <summary>The effective closed dates between two dates, both included.</summary>
    public static async Task<HashSet<DateOnly>> EffectiveNpmClosedDatesAsync(
        this AppDbContext context, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var schedules = await context.NpmOperatingSchedules.AsNoTracking()
            .Where(s => s.EffectiveFrom <= to).ToListAsync(ct);
        var rows = await context.NpmMarketClosures.AsNoTracking()
            .Where(c => c.ClosureDate >= from && c.ClosureDate <= to).ToListAsync(ct);
        return NpmOperatingCalendar.EffectiveClosedDates(from, to, schedules, rows);
    }

    /// <summary>
    /// Every effective closed date from the first recorded closure or schedule up to <paramref name="through"/> (or the latest
    /// explicit row, if later). For readers that have always taken the whole closure history.
    /// </summary>
    public static async Task<HashSet<DateOnly>> EffectiveNpmClosedDatesThroughAsync(
        this AppDbContext context, DateOnly through, CancellationToken ct = default)
    {
        var rows = await context.NpmMarketClosures.AsNoTracking().ToListAsync(ct);
        var latestRow = rows.Count > 0 ? rows.Max(r => r.ClosureDate) : through;
        var end = latestRow > through ? latestRow : through;
        var schedules = await context.NpmOperatingSchedules.AsNoTracking().Where(s => s.EffectiveFrom <= end).ToListAsync(ct);
        var starts = schedules.Select(s => s.EffectiveFrom).Concat(rows.Select(r => r.ClosureDate)).ToList();
        return starts.Count == 0 ? [] : NpmOperatingCalendar.EffectiveClosedDates(starts.Min(), end, schedules, rows);
    }

    /// <summary>Every explicit closure or opening row in range, with the schedules in force, for views that explain WHY a day is closed.</summary>
    public static async Task<(IReadOnlyList<NpmOperatingSchedule> Schedules, IReadOnlyList<NpmMarketClosure> Rows)> NpmCalendarFactsAsync(
        this AppDbContext context, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var schedules = await context.NpmOperatingSchedules.AsNoTracking()
            .Where(s => s.EffectiveFrom <= to).OrderBy(s => s.EffectiveFrom).ToListAsync(ct);
        var rows = await context.NpmMarketClosures.AsNoTracking()
            .Where(c => c.ClosureDate >= from && c.ClosureDate <= to).ToListAsync(ct);
        return (schedules, rows);
    }
}
