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
            await hubContext.Clients.All.SendAsync(
                OnlinePaymentHub.PaymentReceivedEvent, notification, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push online-payment notification (non-critical).");
        }
    }
}
