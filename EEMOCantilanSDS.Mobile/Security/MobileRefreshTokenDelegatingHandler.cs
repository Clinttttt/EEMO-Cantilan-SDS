using System.Net;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.Security;

/// <summary>
/// Auto-refreshes the collector access token on a 401 and retries the original request once. The mobile
/// access token lives ~15 min; while the device is offline that clock keeps running, so the first call
/// after reconnect (e.g. an offline-sync POST) would otherwise fail with 401 until the app restarts.
///
/// <para>Placed in the pipeline just OUTSIDE <see cref="MobileAuthorizationDelegatingHandler"/> so the
/// retry flows back through it and re-attaches the freshly-minted bearer. A semaphore prevents a refresh
/// stampede when several requests 401 at once; if another request already refreshed while we waited, we
/// skip the refresh and just retry with the new token.</para>
/// </summary>
public sealed class MobileRefreshTokenDelegatingHandler(
    ICollectorAuthApiClient authApiClient,
    MobileTokenStore tokenStore) : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // Capture the bearer this request actually used, so we can tell if a peer already refreshed.
        var usedToken = request.Headers.Authorization?.Parameter;

        await tokenStore.InitializeAsync();
        if (!tokenStore.HasRefreshToken)
            return response; // nothing to refresh with → let the caller surface the 401

        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            // Someone else already refreshed while we waited → just retry with the current token.
            if (!string.IsNullOrWhiteSpace(tokenStore.AccessToken) && tokenStore.AccessToken != usedToken)
            {
                response.Dispose();
                return await base.SendAsync(await CloneAsync(request), cancellationToken);
            }

            var refreshResult = await TryRefreshAsync();

            if (refreshResult is null || !refreshResult.IsSuccess || refreshResult.Value is null)
            {
                // A rejected refresh token (400/401 → session truly over) clears the store; a transient
                // error (5xx / network / dead tunnel → null or other code) leaves tokens intact so a later
                // retry can still recover. Either way we never throw — return the original 401.
                if (refreshResult?.StatusCode is 400 or 401)
                {
                    tokenStore.Clear();
                }
                return response;
            }

            await tokenStore.SaveAsync(refreshResult.Value);

            response.Dispose();
            return await base.SendAsync(await CloneAsync(request), cancellationToken);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    // Never throws: a network failure during refresh (e.g. a dropped tunnel) must not surface as an
    // unhandled exception in the original request's caller.
    private async Task<Result<TokenResponseDto>?> TryRefreshAsync()
    {
        try
        {
            return await authApiClient.RefreshTokenAsync(new RefreshTokenCommand
            {
                RefreshToken = tokenStore.RefreshToken!
            });
        }
        catch
        {
            return null;
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

        foreach (var option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        return clone;
    }
}
