using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class NpmOperatingScheduleRepository(AppDbContext context) : INpmOperatingScheduleRepository
{
    public async Task<IReadOnlyList<NpmOperatingSchedule>> GetAllAsync(CancellationToken ct = default) =>
        await context.NpmOperatingSchedules.AsNoTracking().OrderBy(s => s.EffectiveFrom).ToListAsync(ct);

    public Task<NpmOperatingSchedule?> GetByEffectiveDateAsync(DateOnly date, CancellationToken ct = default) =>
        context.NpmOperatingSchedules.FirstOrDefaultAsync(s => s.EffectiveFrom == date, ct);

    public async Task AddAsync(NpmOperatingSchedule schedule, CancellationToken ct = default) =>
        await context.NpmOperatingSchedules.AddAsync(schedule, ct);

    public async Task<IReadOnlyList<NpmMarketClosure>> GetRowsAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await context.NpmMarketClosures.AsNoTracking().Where(c => c.ClosureDate >= from && c.ClosureDate <= to).ToListAsync(ct);

    public async Task<NpmDayImpact> GetDayImpactAsync(DateOnly date, CancellationToken ct = default)
    {
        // The same questions settlement asks of a day: does a term answer for it, was it already paid, was this stall
        // individually excused, and what is this stall's own daily fee. A closure would excuse the unpaid ones and nothing else.
        var stalls = await context.Stalls.AsNoTracking()
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == FacilityCode.NPM && !s.IsDeleted)
            .ToListAsync(ct);
        var billable = stalls.Where(s => s.Contracts.Any(c => c.IsCollectableOn(date))).ToList();
        var ids = billable.Select(s => s.Id).ToList();
        var dailies = (await context.DailyCollections.AsNoTracking()
            .Where(d => d.CollectionDate == date && ids.Contains(d.StallId)).ToListAsync(ct))
            .GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.First());

        var snapshot = await new FeeRateResolver(context).GetSnapshotAsync(ct);
        var paid = 0;
        var unpaid = 0;
        var unpaidAmount = 0m;
        foreach (var stall in billable)
        {
            dailies.TryGetValue(stall.Id, out var daily);
            if (daily is { IsPaid: true }) { paid++; continue; }
            if (daily is { IsAbsent: true }) continue;
            unpaid++;
            unpaidAmount += NpmDailyFee.ForStallOrNull(stall, snapshot, date) ?? 0m;
        }
        return new NpmDayImpact(billable.Count, unpaid, paid, unpaidAmount);
    }
}
