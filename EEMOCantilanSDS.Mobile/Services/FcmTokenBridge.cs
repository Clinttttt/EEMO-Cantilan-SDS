namespace EEMOCantilanSDS.Mobile.Services;

/// <summary>
/// Static bridge between the Android <c>FirebaseMessagingService</c> (instantiated by the OS, outside the
/// DI container) and the app. Holds the current FCM device token and persists it locally so the app can
/// register it with the API after login.
///
/// The FCM token is NOT a credential — it is a per-install push address (like a mailbox number). Storing it
/// in <see cref="Preferences"/> is fine. It is per-device, so it is intentionally NOT keyed by collector:
/// whichever collector is signed in registers the current token under their account (server phase).
/// </summary>
public static class FcmTokenBridge
{
    private const string TokenKey = "fcm_device_token";

    /// <summary>Raised whenever a fresh token arrives (first fetch or a rotation). Server registration hooks here later.</summary>
    public static event Action<string>? TokenRefreshed;

    /// <summary>The last known FCM device token, or null if none has been obtained yet.</summary>
    public static string? CurrentToken
    {
        get
        {
            try
            {
                var token = Preferences.Default.Get(TokenKey, string.Empty);
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Called by the platform messaging service when a token is obtained or rotated.</summary>
    public static void OnTokenRefreshed(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try { Preferences.Default.Set(TokenKey, token); } catch { /* best-effort */ }
        try { TokenRefreshed?.Invoke(token); } catch { /* subscribers must not break the bridge */ }
    }
}
