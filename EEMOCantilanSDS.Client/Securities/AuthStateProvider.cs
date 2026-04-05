using EEMOCantilanSDS.Client.Utilities;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthStateProvider(IHttpContextAccessor httpContextAccessor) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                var accessToken = httpContext.User.FindFirst("AccessToken")?.Value;
                if (!string.IsNullOrEmpty(accessToken))
                {
                    var user = JwtParser.ParseToken(accessToken);
                    if (user != null)
                    {
                        return Task.FromResult(new AuthenticationState(user));
                    }
                }
            }
            
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }
        catch
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }
    }

    public async Task<string?> GetUserIdAsync()
    {
        var state = await GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated == true
            ? state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;
    }

    public Task MarkUserAsAuthenticated()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return Task.CompletedTask;
    }

    public Task MarkUserAsLoggedOut()
    {
        NotifyAuthenticationStateChanged(Anonymous());
        return Task.CompletedTask;
    }

    private static Task<AuthenticationState> Anonymous() =>
        Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity())));
}