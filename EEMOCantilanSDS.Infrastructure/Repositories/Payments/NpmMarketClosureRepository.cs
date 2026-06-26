using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class NpmMarketClosureRepository(AppDbContext context) : INpmMarketClosureRepository
{
    public Task<NpmMarketClosure?> GetAsync(DateOnly date, CancellationToken ct = default) =>
        context.NpmMarketClosures.FirstOrDefaultAsync(x => x.ClosureDate == date, ct);

    public async Task<IReadOnlyList<NpmMarketClosure>> GetByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return await context.NpmMarketClosures
            .AsNoTracking()
            .Where(x => x.ClosureDate >= start && x.ClosureDate <= end)
            .ToListAsync(ct);
    }

    public async Task AddAsync(NpmMarketClosure entity, CancellationToken ct = default) =>
        await context.NpmMarketClosures.AddAsync(entity, ct);

    public void Remove(NpmMarketClosure entity) => context.NpmMarketClosures.Remove(entity);
}
