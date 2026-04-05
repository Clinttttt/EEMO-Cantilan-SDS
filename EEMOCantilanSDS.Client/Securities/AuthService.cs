using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthService(IJSRuntime js, NavigationManager navigation, AuthStateProvider authStateProvider, ILogger<AuthService> logger)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var loginData = new { username, password };
            var json = JsonSerializer.Serialize(loginData);
            
            var success = await js.InvokeAsync<bool>("loginWithCookies", "/api/authproxy/login", json);
            
            if (!success)
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
            await authStateProvider.MarkUserAsLoggedOut();
            navigation.NavigateTo("/login");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during logout");
            throw;
        }
    }
}
