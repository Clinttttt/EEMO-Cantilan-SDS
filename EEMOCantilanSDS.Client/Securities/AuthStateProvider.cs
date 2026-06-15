using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthStateProvider(IHttpContextAccessor httpContextAccessor) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal? _captured;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_captured is not null)
            return Task.FromResult(new AuthenticationState(_captured));

        // Not yet pinned for this scope. During the initial HTTP render (prerender) HttpContext is
        // the genuine per-request context, so reading it here is safe and correct. The interactive
        // circuit pins its principal up-front via CaptureUser() (from the circuit handler), so it
        // never depends on this live read during interactive execution — where IHttpContextAccessor
        // is shared/non-deterministic and could otherwise surface another user.
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            _captured = user;
            return Task.FromResult(new AuthenticationState(user));
        }

        return Task.FromResult(new AuthenticationState(_anonymous));
    }

    /// <summary>
    /// Pins the authenticated principal for this circuit, captured once from the connection's
    /// authenticated context. Subsequent auth-state reads serve this pinned value rather than
    /// re-reading IHttpContextAccessor.
    /// </summary>
    public void CaptureUser(ClaimsPrincipal user)
    {
        _captured = user.Identity?.IsAuthenticated == true ? user : null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
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
        _captured = null;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
        return Task.CompletedTask;
    }
}
