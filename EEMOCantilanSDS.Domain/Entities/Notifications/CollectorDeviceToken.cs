using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Notifications;

/// <summary>
/// A registered push-notification target: one FCM registration token for a device, attributed to the
/// collector currently signed in on it and to their municipality (tenant, via <see cref="IMunicipalityOwned"/>).
///
/// <para>A device has exactly one FCM token (globally unique), so re-registration re-points the existing
/// row to the current collector via <see cref="Reassign"/> rather than creating duplicates. Deliberately a
/// plain <see cref="BaseEntity"/> (not <see cref="AuditableEntity"/>): a device token is a disposable
/// technical record — stale tokens are hard-deleted, which keeps the unique index clean (a soft-deleted row
/// would otherwise collide with a later re-registration of the same token).</para>
/// </summary>
public class CollectorDeviceToken : BaseEntity, IMunicipalityOwned
{
    /// <summary>The collector currently signed in on this device.</summary>
    public Guid CollectorId { get; private set; }

    /// <summary>The FCM registration token (the device's push address).</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>Platform of the device, e.g. "android".</summary>
    public string Platform { get; private set; } = "android";

    public DateTime CreatedAt { get; private set; }

    /// <summary>Last time this token was (re-)registered — used to prune long-idle devices later.</summary>
    public DateTime LastSeenAt { get; private set; }

    /// <summary>Owning municipality (tenant). Stamped by the interceptor on insert when left empty.</summary>
    public Guid MunicipalityId { get; set; }

    private CollectorDeviceToken() { }

    public static CollectorDeviceToken Register(Guid collectorId, string token, string platform, string actor, Guid municipalityId = default)
    {
        return new CollectorDeviceToken
        {
            CollectorId = collectorId,
            Token = token.Trim(),
            Platform = string.IsNullOrWhiteSpace(platform) ? "android" : platform.Trim().ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            MunicipalityId = municipalityId
        };
    }

    /// <summary>Re-point an existing device token to the collector now signed in on that device.</summary>
    public void Reassign(Guid collectorId, Guid municipalityId)
    {
        CollectorId = collectorId;
        if (municipalityId != Guid.Empty)
        {
            MunicipalityId = municipalityId;
        }
        LastSeenAt = DateTime.UtcNow;
    }
}
