using EEMOCantilanSDS.Api.Hubs;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using Microsoft.AspNetCore.SignalR;

namespace EEMOCantilanSDS.Api.Services;

/// <summary>
/// SignalR implementation of <see cref="IPayorRealtimeNotifier"/>. Sends to the per-payor group so only
/// the paying payor is notified. Failures are swallowed so a notification issue can never affect OR
/// encoding (the receipt is already updated in the ledger).
/// </summary>
public sealed class SignalRPayorRealtimeNotifier(
    IHubContext<PayorNotificationHub> hubContext,
    ILogger<SignalRPayorRealtimeNotifier> logger) : IPayorRealtimeNotifier
{
    public async Task NotifyOrIssuedAsync(Guid payorUserId, PayorOrIssuedNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients
                .Group(PayorNotificationHub.GroupFor(payorUserId.ToString()))
                .SendAsync(PayorNotificationHub.OrIssuedEvent, notification, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push payor OR-issued notification (non-critical).");
        }
    }
}
