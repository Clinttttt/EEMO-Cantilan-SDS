using System.Net.Http.Headers;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthorizationDelegatingHandler(IHttpContextAccessor httpContextAccessor, ILogger<AuthorizationDelegatingHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx?.Request.Cookies.TryGetValue("AccessToken", out var token) == true && !string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error attaching auth token");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
