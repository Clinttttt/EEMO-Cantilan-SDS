using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Notifications;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class CollectorDeviceTokenRepository(AppDbContext context) : ICollectorDeviceTokenRepository
{
    public async Task<bool> UpsertAsync(Guid collectorId, string token, string platform, Guid municipalityId, CancellationToken ct = default)
    {
        var trimmed = token.Trim();

        // A device's token is globally unique, so look across tenants (IgnoreQueryFilters) to avoid a duplicate insert
        // (unique index). Handover is allowed inside one municipality only; another LGU's row is never re-pointed.
        var existing = await context.CollectorDeviceTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Token == trimmed, ct);

        if (existing is null)
        {
            // MunicipalityId left empty here is stamped by the MunicipalityStampInterceptor on insert.
            var created = CollectorDeviceToken.Register(collectorId, trimmed, platform, actor: "Collector", municipalityId);
            await context.CollectorDeviceTokens.AddAsync(created, ct);
        }
        else if (existing.MunicipalityId != municipalityId)
        {
            return false;
        }
        else
        {
            existing.Reassign(collectorId, municipalityId);
        }

        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CollectorDeviceToken>> GetByCollectorAsync(Guid collectorId, CancellationToken ct = default) =>
        await context.CollectorDeviceTokens
            .Where(t => t.CollectorId == collectorId)
            .ToListAsync(ct);

    public async Task RemoveByTokenAsync(Guid collectorId, string token, CancellationToken ct = default)
    {
        var trimmed = token.Trim();
        // Tenant-filtered and owner-matched: only the collector's own registration can be dropped.
        var existing = await context.CollectorDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == trimmed && t.CollectorId == collectorId, ct);

        if (existing is not null)
        {
            context.CollectorDeviceTokens.Remove(existing);
            await context.SaveChangesAsync(ct);
        }
    }
}
