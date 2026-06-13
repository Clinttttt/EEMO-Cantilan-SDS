using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Pushes a realtime alert to a SINGLE payor (the one who made the payment). The concrete transport
/// (SignalR) lives in the API layer; the Application only depends on this abstraction. Implementations
/// MUST be best-effort — a notification failure must never affect OR encoding / payment processing.
/// </summary>
public interface IPayorRealtimeNotifier
{
    Task NotifyOrIssuedAsync(Guid payorUserId, PayorOrIssuedNotification notification, CancellationToken ct = default);
}
