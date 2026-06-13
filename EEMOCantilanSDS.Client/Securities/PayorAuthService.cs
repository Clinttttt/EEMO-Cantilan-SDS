using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace EEMOCantilanSDS.Client.Securities;

/// <summary>Client-side payor auth: posts credentials to the payor cookie-proxy, then refreshes
/// the Blazor auth state. Mirrors <see cref="AuthService"/> but for the /payor area.</summary>
public class PayorAuthService(IJSRuntime js, NavigationManager navigation, AuthStateProvider authStateProvider, ILogger<PayorAuthService> logger)
{
    public async Task<bool> ActivateAsync(ActivatePayorAccountCommand command)
    {
        var ok = await PostCookieAsync("/api/payorauthproxy/activate", command);
        if (ok)
        {
            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/payor", forceLoad: true);
        }
        return ok;
    }

    public async Task<bool> LoginAsync(string contactNumber, string password)
    {
        var ok = await PostCookieAsync("/api/payorauthproxy/login",
            new PayorLoginCommand(contactNumber, password));
        if (ok)
        {
            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/payor", forceLoad: true);
        }
        return ok;
    }

    public async Task LogoutAsync()
    {
        try
        {
            await js.InvokeVoidAsync("fetch", "/api/payorauthproxy/logout", new { method = "POST" });
            await authStateProvider.MarkUserAsLoggedOut();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during payor logout");
        }
        navigation.NavigateTo("/payor/login", forceLoad: true);
    }

    private async Task<bool> PostCookieAsync<T>(string url, T payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            return await js.InvokeAsync<bool>("loginWithCookies", url, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during payor auth POST to {Url}", url);
            return false;
        }
    }
}
