using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthorizationDelegatingHandler(
    CircuitServicesAccessor circuitServices,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuthorizationDelegatingHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // Interactive circuit: resolve the *per-circuit* token, which is isolated per user.
            // The circuit's services flow here via CircuitServicesAccessor (handlers otherwise run
            // in a separate, shared DI scope).
            var circuitServiceProvider = circuitServices.Services;
            var token = circuitServiceProvider?.GetService<TokenService>()?.GetToken();

            // Only when there is NO circuit (static prerender, where HttpContext IS the genuine
            // per-request context) do we read the token from the auth cookie. We never use the
            // cookie fallback while a circuit is active — that path is non-deterministic in
            // interactive Blazor Server and can leak another user's token.
            if (string.IsNullOrWhiteSpace(token) && circuitServiceProvider is null)
                token = httpContextAccessor.HttpContext?.User?.FindFirst("AccessToken")?.Value;

            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else
                logger.LogDebug("No access token available to attach");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error attaching auth token");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
