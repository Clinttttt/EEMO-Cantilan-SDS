using global::EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using global::EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using global::EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using global::EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Services;

public sealed class MobileSessionService(
    MobileTokenStore tokenStore,
    ICollectorAuthApiClient collectorAuthApiClient,
    IMobileApiClient mobileApiClient,
    MobilePaymentHubService paymentHub,
    EEMOCantilanSDS.Mobile.Abstractions.IOfflineReadCache readCache)
{
    public MobileMenuDto? Menu { get; private set; }
    public bool IsAuthenticated => tokenStore.HasAccessToken && Menu is not null;
    public string CollectorName => Menu?.CollectorName ?? "Collector";

    // ── Tenant branding (from the menu) with Cantilan-preserving fallbacks ──────────────────────────────
    // When branding is absent/empty (offline pre-load, or a menu cached before branding existed), these
    // return the original Cantilan values so the golden tenant renders byte-for-byte as before.
    public MobileBrandingDto? Branding => Menu?.Branding;

    /// <summary>Seal image source — the tenant's SealPath (data URI/URL) or the local Cantilan seal.</summary>
    public string BrandingSeal =>
        !string.IsNullOrWhiteSpace(Branding?.SealPath) ? Branding!.SealPath! : "images/LGU_CANTILAN_LOGO.jpg";

    /// <summary>Revenue office label for headers/receipts.</summary>
    public string BrandingOffice =>
        !string.IsNullOrWhiteSpace(Branding?.OfficeName) ? Branding!.OfficeName! : "Economic Enterprise & Management Office";

    /// <summary>Short office acronym (compact labels).</summary>
    public string BrandingOfficeAcronym =>
        !string.IsNullOrWhiteSpace(Branding?.OfficeAcronym) ? Branding!.OfficeAcronym! : "EEMO";

    /// <summary>"Municipality of {Name}, {Province}" line for headers/receipts.</summary>
    public string BrandingMunicipalityLine
    {
        get
        {
            var name = Branding?.MunicipalityName;
            if (string.IsNullOrWhiteSpace(name))
                return "Municipality of Cantilan, Surigao del Sur";
            var prov = Branding?.Province;
            return string.IsNullOrWhiteSpace(prov) ? $"Municipality of {name}" : $"Municipality of {name}, {prov}";
        }
    }

    public async Task InitializeAsync()
    {
        await tokenStore.InitializeAsync();

        if (tokenStore.HasAccessToken && Menu is null)
        {
            await LoadMenuAsync(allowRefresh: true);
        }
    }

    public async Task<bool> RestoreSessionAsync()
    {
        await tokenStore.InitializeAsync();

        if (!tokenStore.HasAccessToken && !tokenStore.HasRefreshToken)
        {
            return false;
        }

        if (tokenStore.HasAccessToken)
        {
            var menuError = await LoadMenuAsync(allowRefresh: true);
            return string.IsNullOrWhiteSpace(menuError);
        }

        return await RefreshSessionAsync() && string.IsNullOrWhiteSpace(await LoadMenuAsync(allowRefresh: false));
    }

    public async Task<string?> LoginAsync(string usernameOrEmployeeId, string password, string? municipalityCode = null)
    {
        var result = await collectorAuthApiClient.LoginAsync(new CollectorLoginCommand(
            usernameOrEmployeeId.Trim(),
            password,
            string.IsNullOrWhiteSpace(municipalityCode) ? null : municipalityCode.Trim()));

        if (!result.IsSuccess || result.Value is null)
        {
            return result.Error ?? "Unable to sign in. Please check your username and password.";
        }

        // Clear any previous collector's cached reads BEFORE persisting the new token. If the app dies in
        // this window, the next startup finds an empty cache (safe) rather than the new token paired with
        // the old collector's cached menu/records.
        await readCache.ClearAsync();
        await tokenStore.SaveAsync(result.Value);

        var menuError = await LoadMenuAsync(allowRefresh: true);
        return menuError;
    }

    public async Task<string?> LoadMenuAsync(bool allowRefresh = true)
    {
        var result = await mobileApiClient.GetMenuAsync();

        if (!result.IsSuccess || result.Value is null)
        {
            if (result.StatusCode == 401 && allowRefresh && await RefreshSessionAsync())
            {
                return await LoadMenuAsync(allowRefresh: false);
            }

            // Do NOT clear tokens here on a 401. RefreshSessionAsync already clears them only on a
            // DEFINITIVE rejection (400/401). Clearing here too would also wipe the session on a transient
            // refresh failure (5xx/timeout/offline), locking the collector out and destroying offline access.
            return result.Error ?? "Unable to load assigned collection facilities.";
        }

        Menu = result.Value;
        // Now authenticated with a working session — connect the realtime payment hub.
        // Fire-and-forget + best-effort: the hub is a non-critical enhancement, so a slow/unreachable
        // tunnel must never block (or hang) session/menu loading. It self-heals via its own retry.
        _ = paymentHub.StartAsync();
        EnsureFcmRegistration();
        return null;
    }

    private async Task<bool> RefreshSessionAsync()
    {
        await tokenStore.InitializeAsync();

        if (!tokenStore.HasRefreshToken)
        {
            return false;
        }

        try
        {
            var result = await collectorAuthApiClient.RefreshTokenAsync(new RefreshTokenCommand
            {
                RefreshToken = tokenStore.RefreshToken!
            });

            if (result.IsSuccess && result.Value is not null)
            {
                await tokenStore.SaveAsync(result.Value);
                return true;
            }

            // Only a DEFINITIVE rejection (refresh token invalid/expired) ends the session. Transient
            // server errors (5xx, etc.) must NOT wipe it, or the collector is locked out AND loses access
            // to their cached offline data until they can reach the server again.
            if (result.StatusCode is 400 or 401)
            {
                tokenStore.Clear();
                Menu = null;
            }

            return false;
        }
        catch
        {
            // Network/transient/offline error → keep the session intact so cached offline data stays usable.
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        var refreshToken = tokenStore.RefreshToken;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await collectorAuthApiClient.LogoutAsync(new RefreshTokenCommand
            {
                RefreshToken = refreshToken
            });
        }

        // Clear cached reads first so they can never outlive the session/token.
        await readCache.ClearAsync();
        tokenStore.Clear();
        Menu = null;
        await paymentHub.StopAsync();
    }

    // ── Push notifications: register this device's FCM token with the API ───────────────────────────────
    // The token is captured by the platform (Android) into FcmTokenBridge. Once authenticated we register
    // it under the signed-in collector; a rotation (TokenRefreshed) re-registers. All best-effort: push
    // registration must never block or break login/menu. Non-Android platforms have no token → no-op.
    private bool _fcmHooked;

    private void EnsureFcmRegistration()
    {
        if (!_fcmHooked)
        {
            _fcmHooked = true;
            FcmTokenBridge.TokenRefreshed += token => _ = TryRegisterFcmTokenAsync(token);
        }

        var current = FcmTokenBridge.CurrentToken;
        if (!string.IsNullOrWhiteSpace(current))
        {
            _ = TryRegisterFcmTokenAsync(current);
        }
    }

    private async Task TryRegisterFcmTokenAsync(string token)
    {
        try
        {
            // Skip when the collector has notifications OFF — the token stays unregistered so the server
            // has nothing to push to. Toggling it back on (SetNotificationsEnabledAsync) re-registers.
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(token) || !GetNotificationsEnabled())
            {
                return;
            }

            await mobileApiClient.RegisterDeviceTokenAsync(
                new EEMOCantilanSDS.Application.Requests.Mobile.RegisterDeviceTokenRequest(token, "android"));
        }
        catch
        {
            // Best-effort only — a failed push registration must never surface to the collector.
        }
    }

    // ── Device-local collector preferences ─────────────────────────────────────────────────────────────
    // These are DEVICE-LOCAL and per-collector: keyed by the collector's immutable CollectorId so two
    // collectors sharing a device never see each other's photo/prefs. They deliberately DO NOT live in the
    // offline read cache (which is wiped on every login/logout) — they must survive sessions and work fully
    // offline. Nothing here touches the API, tokens, or the payment path, so the Cantilan golden tenant is
    // unaffected.

    private string CollectorPrefKey =>
        Menu?.CollectorId is { } id && id != Guid.Empty ? id.ToString("N") : "anon";

    private string ProfilePhotoPath =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, $"avatar_{CollectorPrefKey}.txt");

    /// <summary>Returns the locally-stored profile photo (a <c>data:</c> URL) for the current collector, or null.</summary>
    public async Task<string?> GetProfilePhotoAsync()
    {
        try
        {
            var path = ProfilePhotoPath;
            if (System.IO.File.Exists(path))
            {
                var data = await System.IO.File.ReadAllTextAsync(path);
                return string.IsNullOrWhiteSpace(data) ? null : data;
            }
        }
        catch
        {
            // Best-effort: a read failure just means "no saved photo".
        }

        return null;
    }

    /// <summary>Persists the profile photo (a <c>data:</c> URL) locally so it survives re-render, navigation and offline use.</summary>
    public async Task SaveProfilePhotoAsync(string dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            await RemoveProfilePhotoAsync();
            return;
        }

        try
        {
            await System.IO.File.WriteAllTextAsync(ProfilePhotoPath, dataUrl);
        }
        catch
        {
            // Persistence is best-effort; the in-memory preview still shows this run.
        }
    }

    /// <summary>Removes the locally-stored profile photo for the current collector.</summary>
    public Task RemoveProfilePhotoAsync()
    {
        try
        {
            var path = ProfilePhotoPath;
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch
        {
            // Best-effort.
        }

        return Task.CompletedTask;
    }

    private string NotificationsPrefKey => $"notifications_enabled_{CollectorPrefKey}";

    /// <summary>Whether the collector has notifications enabled (defaults to true). Persisted locally.</summary>
    public bool GetNotificationsEnabled() => Preferences.Default.Get(NotificationsPrefKey, true);

    /// <summary>Persists the collector's notifications preference and enforces it server-side: turning it OFF
    /// unregisters this device's push token (so nothing is sent to it); turning it ON re-registers.</summary>
    public async Task SetNotificationsEnabledAsync(bool enabled)
    {
        Preferences.Default.Set(NotificationsPrefKey, enabled);

        var token = FcmTokenBridge.CurrentToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return; // No device token yet (e.g. permission not granted) — nothing to register/remove.
        }

        try
        {
            if (enabled)
            {
                await mobileApiClient.RegisterDeviceTokenAsync(
                    new EEMOCantilanSDS.Application.Requests.Mobile.RegisterDeviceTokenRequest(token, "android"));
            }
            else
            {
                await mobileApiClient.RemoveDeviceTokenAsync(token);
            }
        }
        catch
        {
            // Best-effort — the local preference is saved regardless; registration re-syncs on next login.
        }
    }

    // ── Collector-app binding (which LGU this installed app belongs to) ──────────────────────────────────
    // Set when a bind link is opened (MobileBindBridge → API resolve). DEVICE-WIDE (not per-collector): it
    // scopes login + branding for whoever uses this app. Presentation + login-scoping only — NOT a security
    // boundary (login + LGU-scoped accounts remain the gate). Unbound (Cantilan / single-tenant) → the app
    // behaves exactly as before: existing picker / global login.
    private const string BoundCodeKey = "stalltrack_bound_municipality_code";
    private const string BoundNameKey = "stalltrack_bound_municipality_name";

    /// <summary>The LGU code this app is bound to, or null when unbound.</summary>
    public string? BoundMunicipalityCode
    {
        get
        {
            var code = Preferences.Default.Get(BoundCodeKey, string.Empty);
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
    }

    /// <summary>The bound LGU's display name (best-effort; may be empty), or null when unbound.</summary>
    public string? BoundMunicipalityName
    {
        get
        {
            var name = Preferences.Default.Get(BoundNameKey, string.Empty);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }

    /// <summary>Whether this app has been bound to a specific LGU.</summary>
    public bool IsBound => BoundMunicipalityCode is not null;

    /// <summary>
    /// Resolves a PENDING bind token — from a freshly-opened invite link (deep link) or, as a fallback, the
    /// invite page's clipboard hand-off — to its LGU + branding, WITHOUT persisting it. The login page shows
    /// a branded "confirm your municipality" screen and calls <see cref="ConfirmBind"/> only when the collector
    /// taps Confirm. Best-effort + fail-open: returns null on any failure (no token, offline, unknown/rotated
    /// token) so the app simply stays unbound and the default tenant (Cantilan) path is unchanged.
    /// </summary>
    public async Task<MobileBindInfoDto?> ResolvePendingBindInfoAsync()
    {
        if (IsBound)
            return null;

        // Deep link (MainActivity stored it) takes priority; clipboard hand-off is the fallback.
        var token = MobileBindBridge.TakePendingToken();
        if (string.IsNullOrWhiteSpace(token))
            token = await TryReadClipboardBindTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var result = await mobileApiClient.GetBindInfoAsync(token);
            if (result.IsSuccess && result.Value is { } info && !string.IsNullOrWhiteSpace(info.MunicipalityCode))
                return info;
        }
        catch
        {
            // Offline / unreachable — no confirmation shown; app stays unbound (default applies).
        }
        return null;
    }

    /// <summary>Persists the LGU binding after the collector confirms it on the branded setup screen.</summary>
    public void ConfirmBind(MobileBindInfoDto info)
    {
        if (string.IsNullOrWhiteSpace(info.MunicipalityCode))
            return;
        Preferences.Default.Set(BoundCodeKey, info.MunicipalityCode);
        Preferences.Default.Set(BoundNameKey, info.Name ?? string.Empty);
    }

    /// <summary>
    /// Reads the clipboard hand-off token (the invite page copies <c>stalltrack://a/{token}</c>) as a
    /// fallback for a fresh install opened directly. Read only AFTER the window has focus (the caller invokes
    /// this post-render — Android blocks clipboard reads for an unfocused activity). Capped to a few attempts
    /// so an app kept unbound on purpose (Cantilan/default) stops reading — and stops the paste toast — and
    /// only the exact <c>stalltrack://a/</c> marker is accepted (unrelated clipboard text is ignored).
    /// </summary>
    private async Task<string?> TryReadClipboardBindTokenAsync()
    {
        const string AttemptsKey = "stalltrack_clipboard_bind_attempts";
        var attempts = Preferences.Default.Get(AttemptsKey, 0);
        if (attempts >= 8)
            return null;
        Preferences.Default.Set(AttemptsKey, attempts + 1);

        try
        {
            if (!Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.HasText)
                return null;

            var text = (await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.GetTextAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(text)
                || !text.StartsWith("stalltrack://a/", StringComparison.OrdinalIgnoreCase))
                return null;

            return MobileBindBridge.ExtractToken(text);
        }
        catch
        {
            return null; // Clipboard unavailable / not focused — stay unbound; the default path applies.
        }
    }

    /// <summary>Clears the LGU binding (explicit "switch LGU"). Login then falls back to the picker.</summary>
    public void ClearBinding()
    {
        Preferences.Default.Remove(BoundCodeKey);
        Preferences.Default.Remove(BoundNameKey);
    }

    // ── In-app update check (side-loaded APKs can't self-update; we prompt) ──────────────────────────────
    // Checked once per app session. Best-effort + fail-open: any failure (offline, endpoint unset) returns
    // null → no prompt. Config defaults on the server report version 1 / no minimum, so nothing is prompted
    // until an operator bumps Mobile:LatestVersionCode after publishing a new APK.
    private AppUpdateInfo? _cachedUpdate;
    private bool _updateChecked;

    public async Task<AppUpdateInfo?> GetUpdateInfoAsync()
    {
        if (_updateChecked)
            return _cachedUpdate;
        _updateChecked = true;

        try
        {
            var installed = GetInstalledVersionCode();
            if (installed <= 0)
                return null;

            var result = await mobileApiClient.GetAppVersionAsync();
            if (!result.IsSuccess || result.Value is not { } v)
                return null;

            var available = v.LatestVersionCode > installed;
            var mandatory = v.MinSupportedVersionCode > installed;
            if (!available && !mandatory)
                return null;

            _cachedUpdate = new AppUpdateInfo(true, mandatory, v.ApkUrl, v.LatestVersion, v.Notes);
            return _cachedUpdate;
        }
        catch
        {
            return null;
        }
    }

    private static int GetInstalledVersionCode()
    {
        try
        {
            // On Android, AppInfo.BuildString is the versionCode; VersionString is the versionName.
            return int.TryParse(AppInfo.Current.BuildString, out var code) ? code : 0;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>Result of the in-app update check: whether an update is available and, if so, whether it is
/// mandatory (the installed build is below the minimum supported), plus where to get it.</summary>
public sealed record AppUpdateInfo(bool Available, bool Mandatory, string ApkUrl, string LatestVersion, string? Notes);
