using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTrmCollection;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetMobileTrmCollectionQueryHandlerTests
{
    private static (GetMobileTrmCollectionQueryHandler handler, Mock<ITrmRepository> trmRepo) Build(
        CollectorUser? collector, Guid? collectorId)
    {
        var collectorRepo = new Mock<ICollectorRepository>();
        var trmRepo = new Mock<ITrmRepository>();
        var suggestionRepo = new Mock<ISuggestionRepository>();
        var currentUser = new Mock<ICurrentUserService>();

        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(u => u.CollectorId).Returns(collectorId);

        trmRepo.Setup(r => r.GetKnownPickListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<string>)Array.Empty<string>(),
                           (IReadOnlyList<string>)Array.Empty<string>(),
                           (IReadOnlyList<string>)Array.Empty<string>()));
        suggestionRepo.Setup(r => r.GetHiddenValuesAsync(It.IsAny<SuggestionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<string>)new HashSet<string>());

        return (new GetMobileTrmCollectionQueryHandler(collectorRepo.Object, trmRepo.Object, suggestionRepo.Object, CacheTestDoubles.FeeRateResolver, currentUser.Object), trmRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Tonyo", "EEMO-2026-005", "tonyo", "tonyo@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    [Fact]
    public async Task AssignedToTrm_ReturnsCollection_WithTotals()
    {
        var collector = CollectorWith(FacilityCode.TRM);
        var (handler, trmRepo) = Build(collector, collector.Id);

        trmRepo.Setup(r => r.GetTransportersWithTodayTripsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrmTransporterListDto> { new() { Id = Guid.NewGuid(), Name = "Jeep A" } });
        trmRepo.Setup(r => r.GetTodayTripsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrmTripDto> { new() { Fee = 30m }, new() { Fee = 30m } });

        var result = await handler.Handle(new GetMobileTrmCollectionQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TripsToday);
        Assert.Equal(60m, result.Value.CollectedToday);
        Assert.Equal(30m, result.Value.TripFee);
        Assert.Single(result.Value.Transporters);
    }

    [Fact]
    public async Task NotAssignedToTrm_ReturnsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, _) = Build(collector, collector.Id);

        var result = await handler.Handle(new GetMobileTrmCollectionQuery(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NonCollectorUser_ReturnsForbidden()
    {
        var (handler, _) = Build(collector: null, collectorId: null);

        var result = await handler.Handle(new GetMobileTrmCollectionQuery(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
