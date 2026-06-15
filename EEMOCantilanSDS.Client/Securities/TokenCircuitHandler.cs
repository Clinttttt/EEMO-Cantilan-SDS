using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EEMOCantilanSDS.Client.Securities;

public class TokenCircuitHandler(
    IHttpContextAccessor httpContextAccessor,
    TokenService tokenService,
    AuthStateProvider authStateProvider) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var accessToken = httpContext.User.FindFirst("AccessToken")?.Value;
            if (!string.IsNullOrEmpty(accessToken))
            {
                tokenService.SetToken(accessToken);
            }

            var refreshToken = httpContext.User.FindFirst("RefreshToken")?.Value;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                tokenService.SetRefreshToken(refreshToken);
            }

            // Pin the authenticated principal for this circuit so the auth state no longer depends on
            // live IHttpContextAccessor reads during interactive execution (which can surface another
            // user under concurrency). Captured here from the connection's own authenticated context.
            authStateProvider.CaptureUser(httpContext.User);
        }
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
