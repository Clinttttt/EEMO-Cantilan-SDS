namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// Circuit-scoped counter of in-flight API requests. The global top progress bar subscribes to
/// <see cref="Changed"/> and shows whenever any request is outstanding — a React/NProgress-style
/// "the app is fetching" indicator that requires no per-page wiring. The loading delegating handler
/// brackets every API call with <see cref="Begin"/>/<see cref="End"/>, resolving this per-circuit
/// instance through <see cref="CircuitServicesAccessor"/>.
/// </summary>
public sealed class UiLoadingService
{
    private int _inFlight;

    public bool IsLoading => Volatile.Read(ref _inFlight) > 0;

    public event Action? Changed;

    public void Begin()
    {
        if (Interlocked.Increment(ref _inFlight) == 1)
            Changed?.Invoke();   // first request → show the bar
    }

    public void End()
    {
        var remaining = Interlocked.Decrement(ref _inFlight);
        if (remaining < 0)
            remaining = Interlocked.Exchange(ref _inFlight, 0); // guard against underflow
        if (remaining == 0)
            Changed?.Invoke();   // last request finished → hide the bar
    }
}
