using Microsoft.Maui.Storage;

namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Static bridge for the collector-app <b>bind link</b>. The native (Android) side, on receiving a deep
/// link — <c>https://app.stalltrack.site/a/{token}</c> or the custom scheme <c>stalltrack://a/{token}</c> —
/// hands the URI here; the Blazor login flow later consumes the pending token and resolves it against the
/// API. No DI (mirrors <see cref="FcmTokenBridge"/>).
///
/// <para>Purely presentation + login-scoping — <b>not</b> a security boundary. Login + LGU-scoped accounts
/// remain the real gate, so a mis-bound app still cannot read another LGU's data. Cantilan / single-tenant
/// is unaffected: with no bind link opened, nothing here runs and the existing picker / global login stays.</para>
/// </summary>
public static class MobileBindBridge
{
    private const string PendingTokenKey = "stalltrack_pending_bind_token";

    /// <summary>Raised when a new bind token is captured, so an already-loaded login page can react.</summary>
    public static event Action? PendingTokenChanged;

    /// <summary>Called by the platform with a deep-link URI. Extracts + stores the bind token (best-effort).</summary>
    public static void ReceiveDeepLink(string? uri)
    {
        var token = ExtractToken(uri);
        if (string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            Preferences.Default.Set(PendingTokenKey, token);
        }
        catch
        {
            return; // Storage failure is non-fatal — the app just stays unbound.
        }

        PendingTokenChanged?.Invoke();
    }

    /// <summary>Returns and clears the pending bind token (one-shot), or null when none is pending.</summary>
    public static string? TakePendingToken()
    {
        try
        {
            var token = Preferences.Default.Get(PendingTokenKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(token))
                Preferences.Default.Remove(PendingTokenKey);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extracts the <c>{token}</c> from an <c>…/a/{token}</c> app link or <c>stalltrack://a/{token}</c>.</summary>
    internal static string? ExtractToken(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return null;

        var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // App-link form: https://host/a/{token}
        if (segments.Length >= 2 && string.Equals(segments[0], "a", StringComparison.OrdinalIgnoreCase))
            return segments[1];

        // Custom-scheme form: stalltrack://a/{token}  → Host="a", first path segment is the token.
        if (string.Equals(parsed.Host, "a", StringComparison.OrdinalIgnoreCase) && segments.Length >= 1)
            return segments[0];

        return null;
    }
}
