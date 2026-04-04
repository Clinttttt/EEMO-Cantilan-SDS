using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using System.Net;
using System.Net.Http.Headers;

namespace EEMOCantilanSDS.Client.Securities;

public class RefreshTokenDelegatingHandler(IHttpContextAccessor httpContextAccessor, IAuthApiClient authService, ILogger<RefreshTokenDelegatingHandler> logger) : DelegatingHandler
{
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AttachToken(request);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        await _refreshSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!await RefreshTokenAsync())
                return response;

            AttachToken(request);
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private void AttachToken(HttpRequestMessage request)
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx?.Request.Cookies.TryGetValue("AccessToken", out var token) == true && !string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<bool> RefreshTokenAsync()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx?.Request.Cookies.TryGetValue("RefreshToken", out var refresh) != true || string.IsNullOrWhiteSpace(refresh))
            return false;

        var result = await authService.RefreshTokenAsync(new RefreshTokenCommand { RefreshToken = refresh });
        if (!result.IsSuccess || result.Value is null)
            return false;

        ctx.Items["SetAuthCookies"] = (result.Value.AccessToken!, result.Value.RefreshToken!);
        return true;
    }
}
