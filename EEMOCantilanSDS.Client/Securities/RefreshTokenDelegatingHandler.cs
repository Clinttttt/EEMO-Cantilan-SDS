using System.Net;
using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.Client.Securities;

public class RefreshTokenDelegatingHandler(
    IHttpClientFactory httpClientFactory,
    CircuitServicesAccessor circuitServices,
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
            // Resolve the per-circuit TokenService (isolated per user) via the circuit accessor;
            // handlers otherwise run in a separate, shared DI scope. The cookie claim is used only
            // when there is no circuit (static prerender), never while a circuit is active.
            var circuitTokens = circuitServices.Services?.GetService<TokenService>();
            var refreshToken = circuitTokens?.GetRefreshToken();
            if (string.IsNullOrWhiteSpace(refreshToken) && circuitServices.Services is null)
                refreshToken = httpContextAccessor.HttpContext?.User?.FindFirst("RefreshToken")?.Value;
            if (string.IsNullOrWhiteSpace(refreshToken))
                return response;

            var refreshClient = httpClientFactory.CreateClient("RefreshClient");

            // Refresh against the correct area, decided by the failing request's own URL (reliable in a
            // Blazor circuit, unlike the page path): payor/online-payment APIs renew the payor token.
            var apiPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            var isPayorApi = apiPath.Contains("/api/payor", StringComparison.OrdinalIgnoreCase)
                || apiPath.Contains("/api/onlinepayments", StringComparison.OrdinalIgnoreCase);
            var refreshEndpoint = isPayorApi ? "api/PayorAuth/refresh-token" : "api/AdminAuth/refresh-token";

            var refreshResponse = await refreshClient.PostAsJsonAsync(
                refreshEndpoint, new { RefreshToken = refreshToken }, cancellationToken);

            if (!refreshResponse.IsSuccessStatusCode)
            {
                // Only a REJECTED refresh token (auth failure) means the session is over. Transient
                // errors (5xx/network) must not force a logout — fail this request and let a retry recover.
                if (refreshResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                    NotifySessionExpired();
                return response;
            }

            var tokens = await refreshResponse.Content.ReadFromJsonAsync<TokenResponseDto>(cancellationToken: cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                NotifySessionExpired();
                return response;
            }

            // No rotation: the refresh token is unchanged, only the access token is renewed.
            // Write it back to the per-circuit TokenService so the retry's authorization handler
            // (which reads the same circuit token) reattaches the refreshed bearer.
            circuitTokens?.SetToken(tokens.AccessToken);

            // Retry the original request (a sent request cannot be reused, so clone it).
            return await base.SendAsync(await CloneAsync(request), cancellationToken);
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    // Resolve the circuit's notifier (handlers run in a separate scope, so reach it via the accessor)
    // and signal once. No-op outside a circuit (e.g. static prerender).
    private void NotifySessionExpired() =>
        circuitServices.Services?.GetService<SessionExpiredNotifier>()?.NotifyExpired();

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
