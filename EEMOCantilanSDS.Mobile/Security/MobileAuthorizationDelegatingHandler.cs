using System.Net.Http.Headers;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.Security;

public sealed class MobileAuthorizationDelegatingHandler(MobileTokenStore tokenStore) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await tokenStore.InitializeAsync();

        if (tokenStore.HasAccessToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStore.AccessToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
