using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
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

        return (new RecordTripCommandHandler(trmRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant), trmRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Tonyo", "EEMO-2026-006", "tonyo", "tonyo@eemo.gov", "0917", TestPasswords.Hash("Secret123!"));
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

        Assert.Equal(ResultStatus.Forbidden, result.Status);
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

    // Quick "Record a Trip" — ad-hoc, no transporter registration (TransporterId stays null,
    // and the transporter table is never queried).
    [Fact]
    public async Task AdHocTrip_NoTransporter_RecordsWithoutRegistration()
    {
        var collector = CollectorWith(FacilityCode.TRM);
        var (handler, trmRepo) = Build(collector, "Collector", collector.Id);

        TrmTrip? captured = null;
        trmRepo.Setup(r => r.AddTripAsync(It.IsAny<TrmTrip>(), It.IsAny<CancellationToken>()))
            .Callback<TrmTrip, CancellationToken>((t, _) => captured = t).Returns(Task.CompletedTask);

        var command = new RecordTripCommand(null, "Walk-in Driver", "XYZ 999", "Route 9", "OR-AH-1", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Null(captured!.TransporterId);              // no roster entry
        Assert.Equal("Walk-in Driver", captured.DriverName);
        trmRepo.Verify(r => r.GetTransporterByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The canonical Transportation Cash Ticket service, enabled from a date (IA-050): the legacy trip writer stops from then on.</summary>
    private static async Task<EEMOCantilanSDS.Application.Common.Revenue.TransportationCollectionAuthority> AuthorityAsync(
        bool enabled, DateOnly effective)
    {
        var tenant = Guid.NewGuid();
        var accessor = new Mock<EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor>();
        accessor.SetupGet(a => a.MunicipalityId).Returns(tenant);
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext>()
            .UseInMemoryDatabase($"trm-authority-{Guid.NewGuid():N}")
            .AddInterceptors(new EEMOCantilanSDS.Infrastructure.Persistence.Interceptors.MunicipalityStampInterceptor())
            .Options;
        var db = new EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext(options, accessor.Object);
        var service = EEMOCantilanSDS.Domain.Entities.Revenue.GovernedService.Create(
            tenant, EEMOCantilanSDS.Domain.Constants.CollectorOperationCodes.Transportation, "head");
        db.Add(service);
        db.Add(EEMOCantilanSDS.Domain.Entities.Revenue.GovernedServiceSetting.Create(tenant, service.Id, effective,
            GovernedServiceBasis.VehicleClassRate, null, null, enabled, false, "head"));
        await db.SaveChangesAsync();
        return new EEMOCantilanSDS.Application.Common.Revenue.TransportationCollectionAuthority(db, accessor.Object);
    }

    [Fact]
    public async Task LegacyTripWriter_StopsOnceTheCanonicalTransportationServiceIsEnabled_AndNeverBefore()
    {
        var collector = CollectorWith(FacilityCode.TRM);
        var (before, beforeRepo) = Build(collector, "Collector", collector.Id);
        var past = new EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip.RecordTripCommandHandler(
            beforeRepo.Object, Mock.Of<ICollectorRepository>(c => c.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>()) == Task.FromResult<CollectorUser?>(collector)),
            Mock.Of<ICurrentUserService>(u => u.Role == "Collector" && u.CollectorId == collector.Id && u.Username == "tester"),
            Mock.Of<IUnitOfWork>(), CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant,
            await AuthorityAsync(enabled: true, effective: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)));

        // Enabled only from a future date: today's trip still goes to the legacy writer, nothing is backdated.
        Assert.True((await past.Handle(TripCommand(), CancellationToken.None)).IsSuccess);

        var live = new EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip.RecordTripCommandHandler(
            beforeRepo.Object, Mock.Of<ICollectorRepository>(c => c.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>()) == Task.FromResult<CollectorUser?>(collector)),
            Mock.Of<ICurrentUserService>(u => u.Role == "Collector" && u.CollectorId == collector.Id && u.Username == "tester"),
            Mock.Of<IUnitOfWork>(), CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant,
            await AuthorityAsync(enabled: true, effective: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)));
        var refused = await live.Handle(TripCommand(), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, refused.Status);
        Assert.Contains("canonical Cash Ticket", refused.Error);
        Assert.NotNull(before);
    }
}
