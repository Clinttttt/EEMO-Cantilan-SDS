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

    public async Task<Municipality?> GetByIdentifierAsync(string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        var id = identifier.Trim();
        var idLower = id.ToLowerInvariant();
        var idUpper = id.ToUpperInvariant();

        // Match by TenantCode (subdomain, case-insensitive) or the upper-cased registry Code.
        return await context.Municipalities
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantCode.ToLower() == idLower || m.Code == idUpper, ct);
    }

    public async Task<Municipality?> GetByIdAsync(Guid id, CancellationToken ct)
        => await context.Municipalities
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);
}
