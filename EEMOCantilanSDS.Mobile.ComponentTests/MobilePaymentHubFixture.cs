using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Mobile.Services;

// Test-only hub surface: the real service opens a SignalR connection with the native token store.
// Pages subscribe to these events; the tests never raise them.
public sealed class MobilePaymentHubService
{
#pragma warning disable CS0067
    public event Action<OnlinePaymentNotification>? PaymentReceived;
    public event Action? ConnectionRestored;
#pragma warning restore CS0067
}
