using EEMOCantilanSDS.Api.Hubs;
using EEMOCantilanSDS.Api.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Who is told when an online payment settles.
///
/// <para>
/// The alert carries a payor reference, a billing period and a peso amount. It was sent to <c>Clients.All</c>, so a
/// payment settled for one municipality raised a toast on the screen of every collector and administrator of every
/// other municipality on the platform. On a shared platform that is one LGU's revenue on another LGU's screen.
/// </para>
/// </summary>
public class OnlinePaymentNotifierTenantScopeTests
{
    private static OnlinePaymentNotification Notification(string tenantCode) =>
        new("REF-9001", 2_760m, "2026-08", "GCash", DateTime.UtcNow, Guid.NewGuid(), 2026, 8, tenantCode);

    private static (SignalROnlinePaymentNotifier Notifier, Mock<IHubClients> Clients, Mock<IClientProxy> Proxy) Build()
    {
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);

        var hub = new Mock<IHubContext<OnlinePaymentHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        return (new SignalROnlinePaymentNotifier(hub.Object, NullLogger<SignalROnlinePaymentNotifier>.Instance),
                clients, proxy);
    }

    [Fact]
    public async Task TheAlertGoesToThePayingLguOnly_NotToEveryConnectedClient()
    {
        var (notifier, clients, proxy) = Build();

        await notifier.NotifyPaymentReceivedAsync(Notification("cantilan-sds"), CancellationToken.None);

        clients.Verify(c => c.Group(OnlinePaymentHub.GroupFor("cantilan-sds")), Times.Once);
        clients.Verify(c => c.All, Times.Never);
        proxy.Verify(p => p.SendCoreAsync(
            OnlinePaymentHub.PaymentReceivedEvent, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheGroupNameMatchesWhatTheHubJoins_OrTheAlertReachesNobody()
    {
        // The hub lower-cases and trims the claim when joining; the notifier must address the same string or the
        // message is silently delivered to an empty group.
        Assert.Equal(OnlinePaymentHub.GroupFor("cantilan-sds"), OnlinePaymentHub.GroupFor("  Cantilan-SDS  "));

        var (notifier, clients, _) = Build();
        await notifier.NotifyPaymentReceivedAsync(Notification("  Cantilan-SDS  "), CancellationToken.None);

        clients.Verify(c => c.Group(OnlinePaymentHub.GroupFor("cantilan-sds")), Times.Once);
    }

    [Fact]
    public async Task ANotificationWithNoTenant_ReachesNobodyRatherThanEverybody()
    {
        var (notifier, clients, proxy) = Build();

        await notifier.NotifyPaymentReceivedAsync(Notification(string.Empty), CancellationToken.None);

        clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
        clients.Verify(c => c.All, Times.Never);
        proxy.Verify(p => p.SendCoreAsync(
            It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TwoLgusSettlingPayments_AreAddressedSeparately()
    {
        var (notifier, clients, _) = Build();

        await notifier.NotifyPaymentReceivedAsync(Notification("cantilan-sds"), CancellationToken.None);
        await notifier.NotifyPaymentReceivedAsync(Notification("madrid-sds"), CancellationToken.None);

        clients.Verify(c => c.Group("tenant:cantilan-sds"), Times.Once);
        clients.Verify(c => c.Group("tenant:madrid-sds"), Times.Once);
        Assert.NotEqual(OnlinePaymentHub.GroupFor("cantilan-sds"), OnlinePaymentHub.GroupFor("madrid-sds"));
    }
}
