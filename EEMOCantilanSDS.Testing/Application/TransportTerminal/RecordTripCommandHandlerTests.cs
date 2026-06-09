using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Trip recording is shared by web admins and mobile collectors. Collectors may only record trips
/// if assigned to the transport terminal; admins are unrestricted.
/// </summary>
public class RecordTripCommandHandlerTests
{
    private static (RecordTripCommandHandler handler, Mock<ITrmRepository> trmRepo) Build(
        CollectorUser? collector, string? role, Guid? collectorId)
    {
        var trmRepo = new Mock<ITrmRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        var transporter = TrmTransporter.Create("Jeep A", "Org", "Route 1", "ABC 123");
        trmRepo.Setup(r => r.GetTransporterByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(transporter);
        trmRepo.Setup(r => r.GetNextTripNumberForTodayAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        return (new RecordTripCommandHandler(trmRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object), trmRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Tonyo", "EEMO-2026-006", "tonyo", "tonyo@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    private static RecordTripCommand TripCommand() =>
        new(Guid.NewGuid(), "Driver A", "ABC 123", "Route 1", "OR-1", null);

    [Fact]
    public async Task Collector_NotAssignedToTrm_IsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, trmRepo) = Build(collector, "Collector", collector.Id);

        var result = await handler.Handle(TripCommand(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        trmRepo.Verify(r => r.AddTripAsync(It.IsAny<TrmTrip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_AssignedToTrm_RecordsTrip()
    {
        var collector = CollectorWith(FacilityCode.TRM);
        var (handler, trmRepo) = Build(collector, "Collector", collector.Id);

        TrmTrip? captured = null;
        trmRepo.Setup(r => r.AddTripAsync(It.IsAny<TrmTrip>(), It.IsAny<CancellationToken>()))
            .Callback<TrmTrip, CancellationToken>((t, _) => captured = t).Returns(Task.CompletedTask);

        var result = await handler.Handle(TripCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("Driver A", captured!.DriverName);
        Assert.Equal(collector.Id, captured.CollectorId);
    }
}
