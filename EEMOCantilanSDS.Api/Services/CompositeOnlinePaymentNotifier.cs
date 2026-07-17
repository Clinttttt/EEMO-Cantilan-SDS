using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Api.Services;

/// <summary>
/// Fans an "online payment received" alert out to every notifier: the staff realtime toast (SignalR) and
/// the collector push. Each underlying notifier is independently best-effort, so a failure in one never
/// blocks the other — and the settlement service wraps the whole call in try/catch regardless.
/// </summary>
public sealed class CompositeOnlinePaymentNotifier(
    SignalROnlinePaymentNotifier signalr,
    CollectorPushOnlinePaymentNotifier collectorPush) : IOnlinePaymentNotifier
{
    public async Task NotifyPaymentReceivedAsync(OnlinePaymentNotification notification, CancellationToken cancellationToken = default)
    {
        await signalr.NotifyPaymentReceivedAsync(notification, cancellationToken);
        await collectorPush.NotifyPaymentReceivedAsync(notification, cancellationToken);
    }
}
