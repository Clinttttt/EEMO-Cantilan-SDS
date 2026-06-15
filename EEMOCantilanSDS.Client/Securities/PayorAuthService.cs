using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace EEMOCantilanSDS.Client.Securities;

/// <summary>Client-side payor auth: posts credentials to the payor cookie-proxy, then refreshes
/// the Blazor auth state. Mirrors <see cref="AuthService"/> but for the /payor area.</summary>
public class PayorAuthService(IJSRuntime js, NavigationManager navigation, AuthStateProvider authStateProvider, TokenService tokenService, ILogger<PayorAuthService> logger)
{
    public async Task<(bool Ok, string? Error)> ActivateWithErrorAsync(ActivatePayorAccountCommand command)
    {
        var error = await PostCookieAsync("/api/payorauthproxy/activate", command);
        if (error is null)
        {
            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/payor", forceLoad: true);
            return (true, null);
        }
        return (false, error);
    }

    public async Task<bool> LoginAsync(string contactNumber, string password)
    {
        var error = await PostCookieAsync("/api/payorauthproxy/login",
            new PayorLoginCommand(contactNumber, password));
        if (error is null)
        {
            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/payor", forceLoad: true);
            return true;
        }
        return false;
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
        finally
        {
            tokenService.Clear();
            navigation.NavigateTo("/payor/login", forceLoad: true);
        }
    }

    private async Task<string?> PostCookieAsync<T>(string url, T payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            // loginWithCookies now returns null on success or an error message string on failure.
            return await js.InvokeAsync<string?>("loginWithCookies", url, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during payor auth POST to {Url}", url);
            return "Unable to connect. Please try again.";
        }
    }
}
