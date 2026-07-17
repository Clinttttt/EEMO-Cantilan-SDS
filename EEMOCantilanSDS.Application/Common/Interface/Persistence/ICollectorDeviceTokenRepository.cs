using EEMOCantilanSDS.Domain.Entities.Notifications;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ICollectorDeviceTokenRepository
{
    /// <summary>
    /// Registers a device's FCM token for the given collector, or re-points an existing token (same device)
    /// to that collector. Idempotent per token (unique). Cross-tenant safe: an existing token is found even
    /// if it was previously registered by a collector from another municipality, then re-attributed.
    /// </summary>
    Task UpsertAsync(Guid collectorId, string token, string platform, Guid municipalityId, CancellationToken ct = default);

    /// <summary>Tokens registered to a collector — the send targets when notifying that collector.</summary>
    Task<IReadOnlyList<CollectorDeviceToken>> GetByCollectorAsync(Guid collectorId, CancellationToken ct = default);

    /// <summary>Removes a token FCM reported as unregistered/invalid (called from the send path later).</summary>
    Task RemoveByTokenAsync(string token, CancellationToken ct = default);
}
