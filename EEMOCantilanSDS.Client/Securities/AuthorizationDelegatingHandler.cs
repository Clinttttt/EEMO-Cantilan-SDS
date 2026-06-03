using System.Net.Http.Headers;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthorizationDelegatingHandler(
    TokenService tokenService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuthorizationDelegatingHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // Prefer the in-memory token (kept current by the refresh handler);
            // fall back to the auth-cookie claim when the circuit is first established.
            var token = tokenService.GetToken();
            if (string.IsNullOrWhiteSpace(token))
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
