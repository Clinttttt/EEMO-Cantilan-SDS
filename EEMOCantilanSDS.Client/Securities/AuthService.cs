using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthService(
    IJSRuntime js,
    NavigationManager navigation,
    AuthStateProvider authStateProvider,
    TokenService tokenService,
    ILogger<AuthService> logger)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var loginData = new { username, password };
            var json = JsonSerializer.Serialize(loginData);

            // loginWithCookies returns null on success, or an error message string on failure.
            var error = await js.InvokeAsync<string?>("loginWithCookies", "/api/authproxy/login", json);
            if (error is not null)
            {
                logger.LogWarning("Login failed for user: {Username}", username);
                return false;
            }

            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/menu", forceLoad: true);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during login for user: {Username}", username);
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await js.InvokeVoidAsync("fetch", "/api/authproxy/logout", new { method = "POST" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during logout");
        }
        finally
        {
            // Always complete the local logout even if the server call failed: drop the in-memory
            // tokens and force a full reload so the circuit (and its TokenService) is torn down.
            tokenService.Clear();
            await authStateProvider.MarkUserAsLoggedOut();
            navigation.NavigateTo("/login", forceLoad: true);
        }
    }
}
