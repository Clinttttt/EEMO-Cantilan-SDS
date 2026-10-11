using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class NpmMarketClosureRepository(AppDbContext context) : INpmMarketClosureRepository
{
    public Task<NpmMarketClosure?> GetAsync(DateOnly date, CancellationToken ct = default) =>
        context.NpmMarketClosures.FirstOrDefaultAsync(x => x.ClosureDate == date, ct);

    public async Task<IReadOnlyList<NpmMarketClosure>> GetByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        // The EFFECTIVE closures: explicit closures plus the weekly schedule's closed days, minus any day the Head opened. A
        // scheduled day has no stored row, so it is returned as a read-only row that is never attached to the context.
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var (schedules, rows) = await context.NpmCalendarFactsAsync(start, end, ct);
        var closed = NpmOperatingCalendar.EffectiveClosedDates(start, end, schedules, rows);
        var explicitClosures = rows.Where(r => !r.IsOpenOverride && closed.Contains(r.ClosureDate)).ToList();
        var explicitDates = explicitClosures.Select(r => r.ClosureDate).ToHashSet();
        var scheduled = closed.Where(d => !explicitDates.Contains(d))
            .Select(d => NpmMarketClosure.Create(d, MarketClosureReason.WeeklySchedule, "Weekly operating schedule", "System"));
        return explicitClosures.Concat(scheduled).OrderBy(x => x.ClosureDate).ToList();
    }

    public async Task AddAsync(NpmMarketClosure entity, CancellationToken ct = default) =>
        await context.NpmMarketClosures.AddAsync(entity, ct);

    public void Remove(NpmMarketClosure entity) => context.NpmMarketClosures.Remove(entity);
}
