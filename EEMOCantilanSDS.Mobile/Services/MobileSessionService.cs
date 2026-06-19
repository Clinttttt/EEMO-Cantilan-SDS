using global::EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using global::EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using global::EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using global::EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Services;

public sealed class MobileSessionService(
    MobileTokenStore tokenStore,
    ICollectorAuthApiClient collectorAuthApiClient,
    IMobileApiClient mobileApiClient,
    MobilePaymentHubService paymentHub)
{
    public MobileMenuDto? Menu { get; private set; }
    public bool IsAuthenticated => tokenStore.HasAccessToken && Menu is not null;
    public string CollectorName => Menu?.CollectorName ?? "Collector";

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

    public async Task<string?> LoginAsync(string usernameOrEmployeeId, string password)
    {
        var result = await collectorAuthApiClient.LoginAsync(new CollectorLoginCommand(
            usernameOrEmployeeId.Trim(),
            password));

        if (!result.IsSuccess || result.Value is null)
        {
            return result.Error ?? "Unable to sign in. Please check your username and password.";
        }

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

            if (result.StatusCode == 401)
            {
                tokenStore.Clear();
                Menu = null;
            }

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

        var result = await collectorAuthApiClient.RefreshTokenAsync(new RefreshTokenCommand
        {
            RefreshToken = tokenStore.RefreshToken!
        });

        if (!result.IsSuccess || result.Value is null)
        {
            tokenStore.Clear();
            Menu = null;
            return false;
        }

        await tokenStore.SaveAsync(result.Value);
        return true;
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

        tokenStore.Clear();
        Menu = null;
        await paymentHub.StopAsync();
    }
}
