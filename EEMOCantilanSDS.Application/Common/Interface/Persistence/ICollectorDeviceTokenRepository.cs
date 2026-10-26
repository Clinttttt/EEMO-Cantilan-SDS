using EEMOCantilanSDS.Domain.Entities.Notifications;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ICollectorDeviceTokenRepository
{
    /// <summary>
    /// Registers a device's FCM token for the given collector, or re-points an existing token (same device)
    /// to that collector. Idempotent per token (unique). The token is globally unique, so an existing row is
    /// found across tenants, but it is only re-pointed inside the SAME municipality (device handover between
    /// collectors of one LGU). A token held by another municipality is never taken over: returns false.
    /// </summary>
    Task<bool> UpsertAsync(Guid collectorId, string token, string platform, Guid municipalityId, CancellationToken ct = default);

    /// <summary>Tokens registered to a collector — the send targets when notifying that collector.</summary>
    Task<IReadOnlyList<CollectorDeviceToken>> GetByCollectorAsync(Guid collectorId, CancellationToken ct = default);

    /// <summary>Removes the collector's own token (sign-out, or FCM reported it invalid). Another collector's or tenant's token is untouched.</summary>
    Task RemoveByTokenAsync(Guid collectorId, string token, CancellationToken ct = default);
}
