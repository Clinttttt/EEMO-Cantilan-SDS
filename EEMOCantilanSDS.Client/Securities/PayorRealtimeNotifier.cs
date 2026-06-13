namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// In-circuit bridge for payor realtime events. The single SignalR connection lives in
/// <c>PayorOrToaster</c>; it republishes events through this service so any open payor page (e.g.
/// History) can refresh itself — flipping a receipt from provisional to official without a manual
/// reload. Registered SCOPED: in Blazor Server a scope == one circuit, so the toaster and the page the
/// payor is viewing share the same instance.
/// </summary>
public sealed class PayorRealtimeNotifier
{
    /// <summary>Raised (on the circuit's sync context) when this payor's Official Receipt is encoded.</summary>
    public event Action? OrIssued;

    public void PublishOrIssued() => OrIssued?.Invoke();
}
