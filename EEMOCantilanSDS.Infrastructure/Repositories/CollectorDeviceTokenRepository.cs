using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Notifications;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class CollectorDeviceTokenRepository(AppDbContext context) : ICollectorDeviceTokenRepository
{
    public async Task UpsertAsync(Guid collectorId, string token, string platform, Guid municipalityId, CancellationToken ct = default)
    {
        var trimmed = token.Trim();

        // A device's token is globally unique. Look across tenants (IgnoreQueryFilters) because the same
        // physical device may previously have been used by a collector from another LGU — re-point that
        // row rather than inserting a duplicate (which would violate the unique index).
        var existing = await context.CollectorDeviceTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == trimmed, ct);

        if (existing is null)
        {
            // MunicipalityId left empty here is stamped by the MunicipalityStampInterceptor on insert.
            var created = CollectorDeviceToken.Register(collectorId, trimmed, platform, actor: "Collector", municipalityId);
            await context.CollectorDeviceTokens.AddAsync(created, ct);
        }
        else
        {
            existing.Reassign(collectorId, municipalityId);
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CollectorDeviceToken>> GetByCollectorAsync(Guid collectorId, CancellationToken ct = default) =>
        await context.CollectorDeviceTokens
            .Where(t => t.CollectorId == collectorId)
            .ToListAsync(ct);

    public async Task RemoveByTokenAsync(string token, CancellationToken ct = default)
    {
        var trimmed = token.Trim();
        var existing = await context.CollectorDeviceTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == trimmed, ct);

        if (existing is not null)
        {
            context.CollectorDeviceTokens.Remove(existing);
            await context.SaveChangesAsync(ct);
        }
    }
}
