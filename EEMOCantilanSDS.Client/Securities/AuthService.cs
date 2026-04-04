using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.AspNetCore.Components;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthService(IAuthApiClient authService, NavigationManager navigation, AuthStateProvider authStateProvider, IHttpContextAccessor httpContextAccessor, ILogger<AuthService> logger)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var result = await authService.LoginAsync(new LoginCommand(username, password));

            if (!result.IsSuccess || result.Value is null)
            {
                logger.LogWarning("Login failed for user: {Username}", username);
                return false;
            }

            if (httpContextAccessor.HttpContext is not null)
            {
                httpContextAccessor.HttpContext.Items["SetAuthCookies"] = (result.Value.AccessToken!, result.Value.RefreshToken!);
            }

            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/menu");
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
            await authService.LogoutAsync();

            if (httpContextAccessor.HttpContext is not null)
            {
                httpContextAccessor.HttpContext.Items["ClearAuthCookies"] = true;
            }

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
