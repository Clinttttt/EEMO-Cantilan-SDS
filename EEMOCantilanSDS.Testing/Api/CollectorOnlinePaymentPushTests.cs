using EEMOCantilanSDS.Api.Services;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EEMOCantilanSDS.Testing;

public sealed class CollectorOnlinePaymentPushTests
{
    [Fact]
    public async Task VerifiedStallPayment_NotifiesActiveCollectorsAssignedToItsFacility()
    {
        var stallId = Guid.NewGuid();
        var assigned1 = Guid.NewGuid();
        var assigned2 = Guid.NewGuid();
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(x => x.GetFacilityCodeByStallIdAsync(stallId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FacilityCode.TCC);
        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(x => x.GetActiveCollectorIdsByFacilityAsync(FacilityCode.TCC, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { assigned1, assigned2 });
        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(x => x.GetFacilityNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<FacilityCode, string> { [FacilityCode.TCC] = "Commercial Center" });
        var push = new Mock<IPushSender>();
        push.Setup(x => x.SendToCollectorAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var sender = new CollectorPushOnlinePaymentNotifier(
            push.Object, stalls.Object, collectors.Object, facilities.Object,
            NullLogger<CollectorPushOnlinePaymentNotifier>.Instance);

        await sender.NotifyPaymentReceivedAsync(new OnlinePaymentNotification(
            "OP-TEST", 1200m, "2026-10", "QRPH", DateTime.UtcNow, stallId, 2026, 10, "cantilan-sds"));

        foreach (var collectorId in new[] { assigned1, assigned2 })
            push.Verify(x => x.SendToCollectorAsync(collectorId,
                "Online payment received", It.Is<string>(body =>
                    body.Contains("1,200") && body.Contains("Commercial Center") && body.Contains("OR number")),
                null, It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(x => x.SendToCollectorAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        collectors.Verify(x => x.GetActiveCollectorIdsByFacilityAsync(FacilityCode.TCC,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoCollectorsAssignedToPaidFacility_DoesNotSendToAnotherFacility()
    {
        var stallId = Guid.NewGuid();
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(x => x.GetFacilityCodeByStallIdAsync(stallId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FacilityCode.TCC);
        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(x => x.GetActiveCollectorIdsByFacilityAsync(FacilityCode.TCC,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        var push = new Mock<IPushSender>();
        var sender = new CollectorPushOnlinePaymentNotifier(
            push.Object, stalls.Object, collectors.Object, new Mock<IFacilityRepository>().Object,
            NullLogger<CollectorPushOnlinePaymentNotifier>.Instance);

        await sender.NotifyPaymentReceivedAsync(new OnlinePaymentNotification(
            "OP-TEST", 1200m, "2026-10", "QRPH", DateTime.UtcNow, stallId, 2026, 10, "cantilan-sds"));

        push.Verify(x => x.SendToCollectorAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
