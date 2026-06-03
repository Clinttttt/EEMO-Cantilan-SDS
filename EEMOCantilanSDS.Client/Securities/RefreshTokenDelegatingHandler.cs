using System.Net;
using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Dtos;

namespace EEMOCantilanSDS.Client.Securities;

public class RefreshTokenDelegatingHandler(
    IHttpClientFactory httpClientFactory,
    TokenService tokenService,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
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
            // The refresh token lives as a claim in the auth cookie; the per-circuit TokenService is
            // not shared with this handler's scope, so the cookie claim is the reliable source.
            var refreshToken = httpContextAccessor.HttpContext?.User?.FindFirst("RefreshToken")?.Value
                ?? tokenService.GetRefreshToken();
            if (string.IsNullOrWhiteSpace(refreshToken))
                return response;

            var refreshClient = httpClientFactory.CreateClient("RefreshClient");
            var refreshResponse = await refreshClient.PostAsJsonAsync(
                "api/AdminAuth/refresh-token", new { RefreshToken = refreshToken }, cancellationToken);

            if (!refreshResponse.IsSuccessStatusCode)
                return response;

            var tokens = await refreshResponse.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
                return response;

            // No rotation: the refresh token is unchanged, only the access token is renewed.
            tokenService.SetToken(tokens.AccessToken);

            // Retry the original request (a sent request cannot be reused, so clone it);
            // the authorization handler reattaches the refreshed bearer token.
            return await base.SendAsync(await CloneAsync(request), cancellationToken);
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
