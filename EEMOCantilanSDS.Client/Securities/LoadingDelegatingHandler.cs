using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// Marks the circuit as "loading" for the lifetime of each API request so the global progress bar
/// reflects real in-flight data fetches. The per-circuit <see cref="UiLoadingService"/> is resolved
/// via <see cref="CircuitServicesAccessor"/> (handlers run outside the circuit's DI scope). Outside a
/// circuit (e.g. static prerender) it is a no-op.
/// </summary>
public class LoadingDelegatingHandler(CircuitServicesAccessor circuitServices) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var loading = circuitServices.Services?.GetService<UiLoadingService>();
        loading?.Begin();
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            loading?.End();
        }
    }
}
