using System.Net;

namespace EEMOCantilanSDS.Client.Securities;

public class RefreshTokenDelegatingHandler(TokenService tokenService, IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        await _refreshSemaphore.WaitAsync(cancellationToken);
        try
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx?.Request.Cookies.TryGetValue("RefreshToken", out var refresh) != true || string.IsNullOrWhiteSpace(refresh))
                return response;

            var refreshResponse = await ctx.RequestServices.GetRequiredService<HttpClient>()
                .PostAsJsonAsync("/api/authproxy/refresh", new { RefreshToken = refresh }, cancellationToken);

            if (!refreshResponse.IsSuccessStatusCode)
                return response;

            var newToken = await refreshResponse.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(newToken))
                return response;

            tokenService.SetToken(newToken);
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }
}
