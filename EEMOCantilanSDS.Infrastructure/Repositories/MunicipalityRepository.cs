using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class MunicipalityRepository(AppDbContext context) : IMunicipalityRepository
{
    public async Task<IReadOnlyList<Municipality>> GetAllAsync(CancellationToken ct)
        => await context.Municipalities
            .AsNoTracking()
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.Name)
            .ToListAsync(ct);

    public async Task<Municipality?> GetDefaultAsync(CancellationToken ct)
        => await context.Municipalities
            .AsNoTracking()
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.Name)
            .FirstOrDefaultAsync(ct);
}
