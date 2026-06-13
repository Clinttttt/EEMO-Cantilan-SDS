using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Pushes a realtime "online payment received" alert to connected staff. The concrete transport
/// (SignalR) lives in the API layer; the Application only depends on this abstraction. Implementations
/// MUST be best-effort — a notification failure must never affect payment processing.
/// </summary>
public interface IOnlinePaymentNotifier
{
    Task NotifyPaymentReceivedAsync(OnlinePaymentNotification notification, CancellationToken cancellationToken = default);
}
