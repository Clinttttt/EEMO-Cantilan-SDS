using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class DailyCollectionRepository(AppDbContext context) : IDailyCollectionRepository
{
    public async Task<DailyCollection?> GetByStallAndDateAsync(Guid stallId, DateOnly collectionDate, CancellationToken ct = default)
    {
        return await context.DailyCollections
            .FirstOrDefaultAsync(d => d.StallId == stallId && d.CollectionDate == collectionDate, ct);
    }

    public async Task<IReadOnlyList<DailyCollection>> GetByStallAndMonthAsync(Guid stallId, int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        return await context.DailyCollections
            .AsNoTracking()
            .Where(d => d.StallId == stallId && d.CollectionDate >= startDate && d.CollectionDate <= endDate)
            .OrderBy(d => d.CollectionDate)
            .ToListAsync(ct);
    }

    public async Task AddAsync(DailyCollection dailyCollection, CancellationToken ct = default)
    {
        await context.DailyCollections.AddAsync(dailyCollection, ct);
    }
}
