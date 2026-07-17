using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Api.Services;

/// <summary>
/// <see cref="IOnlinePaymentNotifier"/> that pushes a notification to the collector(s) assigned to the
/// facility of the paid stall, prompting them to enter the OR number. Strictly best-effort: any failure is
/// swallowed so it can never affect payment settlement (the caller also wraps this in try/catch).
/// </summary>
public sealed class CollectorPushOnlinePaymentNotifier(
    IPushSender pushSender,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ILogger<CollectorPushOnlinePaymentNotifier> logger) : IOnlinePaymentNotifier
{
    public async Task NotifyPaymentReceivedAsync(OnlinePaymentNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            var facility = await stallRepository.GetFacilityCodeByStallIdAsync(notification.StallId, cancellationToken);
            if (facility is null)
            {
                return;
            }

            var collectorIds = await collectorRepository.GetActiveCollectorIdsByFacilityAsync(facility.Value, cancellationToken);
            if (collectorIds.Count == 0)
            {
                return;
            }

            const string title = "Online payment received";
            var body = $"A payor paid \u20b1{notification.Amount:N0} online at {FacilityDisplayNames.Of(facility.Value)}. Open StallTrack to enter the OR number.";

            foreach (var collectorId in collectorIds)
            {
                await pushSender.SendToCollectorAsync(collectorId, title, body, data: null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push online-payment collector notification (non-critical).");
        }
    }
}
