using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EEMOCantilanSDS.Client.Securities;

public class TokenCircuitHandler(IHttpContextAccessor httpContextAccessor, TokenService tokenService) : CircuitHandler
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
        }
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
