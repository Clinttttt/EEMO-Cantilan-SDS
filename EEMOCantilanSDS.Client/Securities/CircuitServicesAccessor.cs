using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// Exposes the *current circuit's* service provider to code that runs outside the circuit's DI
/// scope — notably <see cref="System.Net.Http.DelegatingHandler"/>s created by IHttpClientFactory,
/// which live in their own (shared) handler scope. This lets those handlers resolve the per-circuit
/// <see cref="TokenService"/> so each user's API calls carry that user's own token.
///
/// The value flows via <see cref="AsyncLocal{T}"/>, which is ambient to the executing circuit
/// activity, so it is isolated per circuit and safe under concurrent users.
/// </summary>
public sealed class CircuitServicesAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> _services = new();

    public IServiceProvider? Services
    {
        get => _services.Value;
        set => _services.Value = value;
    }
}

/// <summary>
/// Sets <see cref="CircuitServicesAccessor.Services"/> to the circuit's scoped provider for the
/// duration of each inbound circuit activity (e.g. a component lifecycle method or event handler),
/// so awaited HTTP calls made during that activity can reach the circuit's services.
/// </summary>
public sealed class ServicesAccessorCircuitHandler(
    IServiceProvider services,
    CircuitServicesAccessor accessor) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            accessor.Services = services;
            try
            {
                await next(context);
            }
            finally
            {
                accessor.Services = null;
            }
        };
}
