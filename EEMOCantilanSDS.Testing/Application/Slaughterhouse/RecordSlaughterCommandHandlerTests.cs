using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Slaughter recording is shared by web admins and mobile collectors. Collectors may only record
/// at the slaughterhouse if assigned to it; admins are unrestricted.
/// </summary>
public class RecordSlaughterCommandHandlerTests
{
    private static (RecordSlaughterCommandHandler handler, Mock<ISlaughterRepository> slaughterRepo) Build(
        CollectorUser? collector, string? role, Guid? collectorId)
    {
        var slaughterRepo = new Mock<ISlaughterRepository>();
        var facilityRepo = new Mock<IFacilityRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        facilityRepo.Setup(r => r.GetByCodeAsync(FacilityCode.SLH, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH"));
        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        return (new RecordSlaughterCommandHandler(slaughterRepo.Object, facilityRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant), slaughterRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Ramon", "EEMO-2026-004", "ramon", "ramon@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    private static RecordSlaughterCommand HogCommand() =>
        new("Owner A", new DateOnly(2026, 6, 9), "OR-1", AnimalType.Hog, null, 2, null);

    [Fact]
    public async Task Collector_NotAssignedToSlh_IsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, slaughterRepo) = Build(collector, "Collector", collector.Id);

        var result = await handler.Handle(HogCommand(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        slaughterRepo.Verify(r => r.AddAsync(It.IsAny<SlaughterTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_AssignedToSlh_RecordsTransaction()
    {
        var collector = CollectorWith(FacilityCode.SLH);
        var (handler, slaughterRepo) = Build(collector, "Collector", collector.Id);

        SlaughterTransaction? captured = null;
        slaughterRepo.Setup(r => r.AddAsync(It.IsAny<SlaughterTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<SlaughterTransaction, CancellationToken>((t, _) => captured = t).Returns(Task.CompletedTask);

        var result = await handler.Handle(HogCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(AnimalType.Hog, captured!.AnimalType);
        Assert.Equal(2, captured.NumberOfHeads);
        Assert.Equal(collector.Id, captured.CollectorId);
    }
}
