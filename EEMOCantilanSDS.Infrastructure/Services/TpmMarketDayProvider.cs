using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Reads the current office's weekly market day, as it stood on a given date.
///
/// <para>
/// The office's schedule rows come first: the latest one taking effect on or before the date asked about. An
/// office that has never moved its day has none, and is answered from its registry record; one that never stated
/// a day is answered Friday. The registry record is deliberately NOT consulted when schedule rows exist, because
/// it holds the day in force now, and asking about an earlier date must not be answered with today's arrangement.
/// </para>
/// </summary>
public class TpmMarketDayProvider(IAppDbContext context, ITenantContext tenantContext) : ITpmMarketDayProvider
{
    public async Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var scheduled = await context.TpmMarketDaySchedules
            .Where(s => s.EffectiveFrom <= asOf)
            .OrderByDescending(s => s.EffectiveFrom)
            .Select(s => (DayOfWeek?)s.Day)
            .FirstOrDefaultAsync(ct);

        return scheduled ?? await RegisteredDayAsync(ct);
    }

    public async Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        // Read the whole schedule once, then walk the month. Asking per date would be a query per day, and the
        // answer has to be able to change part-way through the month anyway.
        var schedule = await context.TpmMarketDaySchedules
            .Where(s => s.EffectiveFrom <= last)
            .OrderBy(s => s.EffectiveFrom)
            .Select(s => new { s.EffectiveFrom, s.Day })
            .ToListAsync(ct);

        var registered = await RegisteredDayAsync(ct);

        var dates = new List<DateOnly>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            DayOfWeek? inForce = null;
            foreach (var row in schedule)
            {
                if (row.EffectiveFrom > date) break;
                inForce = row.Day;
            }

            if (date.DayOfWeek == (inForce ?? registered)) dates.Add(date);
        }

        return dates;
    }

    /// <summary>The day on the office's registry record: what it stated at activation, or Friday if it stated none.</summary>
    private async Task<DayOfWeek> RegisteredDayAsync(CancellationToken ct)
    {
        var code = tenantContext.TenantCode;
        var day = await context.Municipalities
            .IgnoreQueryFilters()
            .Where(m => m.TenantCode == code)
            .Select(m => m.TpmMarketDay)
            .FirstOrDefaultAsync(ct);

        return day ?? DayOfWeek.Friday;
    }
}
