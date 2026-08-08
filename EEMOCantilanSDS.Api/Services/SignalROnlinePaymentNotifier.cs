using EEMOCantilanSDS.Api.Hubs;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using Microsoft.AspNetCore.SignalR;

namespace EEMOCantilanSDS.Api.Services;

/// <summary>
/// SignalR implementation of <see cref="IOnlinePaymentNotifier"/>. Keeps the SignalR dependency in the
/// API layer; the Application only knows the abstraction. Failures are swallowed so a notification issue
/// can never affect payment processing.
/// </summary>
public sealed class SignalROnlinePaymentNotifier(
    IHubContext<OnlinePaymentHub> hubContext,
    ILogger<SignalROnlinePaymentNotifier> logger) : IOnlinePaymentNotifier
{
    public async Task NotifyPaymentReceivedAsync(OnlinePaymentNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            // Addressed to the paying LGU's own group. This used to be Clients.All, so a payment settled for one
            // municipality raised a toast carrying the payor reference, billing period and peso amount on every
            // collector's and administrator's screen across every municipality on the platform. A notification with no
            // tenant on it reaches nobody rather than everybody.
            if (string.IsNullOrWhiteSpace(notification.TenantCode))
            {
                logger.LogWarning(
                    "Online-payment notification for reference {Reference} carried no tenant, so it was not delivered.",
                    notification.Reference);
                return;
            }

            await hubContext.Clients
                .Group(OnlinePaymentHub.GroupFor(notification.TenantCode))
                .SendAsync(OnlinePaymentHub.PaymentReceivedEvent, notification, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push online-payment notification (non-critical).");
        }
    }
}
