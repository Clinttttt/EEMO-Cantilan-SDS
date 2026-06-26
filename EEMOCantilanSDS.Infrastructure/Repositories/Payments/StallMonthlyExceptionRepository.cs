using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class StallMonthlyExceptionRepository(AppDbContext context) : IStallMonthlyExceptionRepository
{
    public Task<StallMonthlyException?> GetAsync(Guid stallId, int year, int month, CancellationToken ct = default) =>
        context.StallMonthlyExceptions
            .FirstOrDefaultAsync(x => x.StallId == stallId && x.BillingYear == year && x.BillingMonth == month, ct);

    public async Task<IReadOnlyList<StallMonthlyException>> GetByStallYearAsync(Guid stallId, int year, CancellationToken ct = default) =>
        await context.StallMonthlyExceptions
            .AsNoTracking()
            .Where(x => x.StallId == stallId && x.BillingYear == year)
            .ToListAsync(ct);

    public async Task AddAsync(StallMonthlyException entity, CancellationToken ct = default) =>
        await context.StallMonthlyExceptions.AddAsync(entity, ct);

    public void Remove(StallMonthlyException entity) => context.StallMonthlyExceptions.Remove(entity);
}
