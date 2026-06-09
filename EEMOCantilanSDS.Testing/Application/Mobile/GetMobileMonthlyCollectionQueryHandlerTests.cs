using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetMobileMonthlyCollectionQueryHandlerTests
{
    private static (GetMobileMonthlyCollectionQueryHandler handler, Mock<IStallRepository> stallRepo) Build(
        CollectorUser? collector, Guid? collectorId)
    {
        var collectorRepo = new Mock<ICollectorRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var currentUser = new Mock<ICurrentUserService>();

        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(u => u.CollectorId).Returns(collectorId);

        return (new GetMobileMonthlyCollectionQueryHandler(collectorRepo.Object, stallRepo.Object, currentUser.Object), stallRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Maria Collector", "EEMO-2026-002", "maria", "maria@eemo.gov", "09180000000", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    [Fact]
    public async Task AssignedMonthlyFacility_ReturnsCollection()
    {
        var collector = CollectorWith(FacilityCode.TCC);
        var (handler, stallRepo) = Build(collector, collector.Id);
        stallRepo.Setup(r => r.GetMobileMonthlyCollectionAsync(FacilityCode.TCC, 2026, 6, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileMonthlyCollectionDto(FacilityCode.TCC, "Town Center Commercial", 2026, 6,
                new DateOnly(2026, 6, 9), 0, 0, 0, 0, 0m, 0m, Array.Empty<MobileMonthlyStallCollectionDto>()));

        var result = await handler.Handle(new GetMobileMonthlyCollectionQuery(FacilityCode.TCC, 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FacilityCode.TCC, result.Value!.Facility);
    }

    [Fact]
    public async Task UnassignedFacility_ReturnsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NCC); // not TCC
        var (handler, _) = Build(collector, collector.Id);

        var result = await handler.Handle(new GetMobileMonthlyCollectionQuery(FacilityCode.TCC, 2026, 6), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NonMonthlyFacility_ReturnsForbidden()
    {
        // NPM is daily-collected; SLH/TRM/TPM are per-transaction — none belong to this query.
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, _) = Build(collector, collector.Id);

        var result = await handler.Handle(new GetMobileMonthlyCollectionQuery(FacilityCode.NPM, 2026, 6), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NonCollectorUser_ReturnsForbidden()
    {
        var (handler, _) = Build(collector: null, collectorId: null);

        var result = await handler.Handle(new GetMobileMonthlyCollectionQuery(FacilityCode.TCC, 2026, 6), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
