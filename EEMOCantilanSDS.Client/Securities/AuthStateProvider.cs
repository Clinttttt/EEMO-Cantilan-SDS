using EEMOCantilanSDS.Client.Utilities;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthStateProvider(IHttpContextAccessor httpContextAccessor) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(new AuthenticationState(httpContext.User));
        }
        
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
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