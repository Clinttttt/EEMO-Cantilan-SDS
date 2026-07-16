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
}
