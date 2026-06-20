using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.Platform;

/// <summary>
/// MAUI-backed <see cref="ICurrentCollectorProvider"/>. Uses the signed-in collector's immutable
/// <c>CollectorId</c> GUID from the active session menu (loaded online, then served from cache offline)
/// as the ownership key — this matches the server's own attribution and, unlike Employee ID, never
/// changes. Returns <c>null</c> when no session is loaded, so queued ops are never synced under an
/// unknown identity.
/// </summary>
public sealed class MauiCurrentCollectorProvider(MobileSessionService session) : ICurrentCollectorProvider
{
    public string? CollectorKey
    {
        get
        {
            var id = session.Menu?.CollectorId;
            return id is { } guid && guid != Guid.Empty ? guid.ToString() : null;
        }
    }
}
