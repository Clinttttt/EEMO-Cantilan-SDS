using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileSlaughterCollection;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetMobileSlaughterCollectionQueryHandlerTests
{
    private static (GetMobileSlaughterCollectionQueryHandler handler, Mock<ISlaughterRepository> slaughterRepo) Build(
        CollectorUser? collector, Guid? collectorId)
    {
        var collectorRepo = new Mock<ICollectorRepository>();
        var slaughterRepo = new Mock<ISlaughterRepository>();
        var suggestionRepo = new Mock<ISuggestionRepository>();
        var currentUser = new Mock<ICurrentUserService>();

        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(u => u.CollectorId).Returns(collectorId);

        suggestionRepo.Setup(r => r.GetHiddenValuesAsync(It.IsAny<SuggestionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<string>)new HashSet<string>());

        return (new GetMobileSlaughterCollectionQueryHandler(collectorRepo.Object, slaughterRepo.Object, suggestionRepo.Object, currentUser.Object), slaughterRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Ramon", "EEMO-2026-003", "ramon", "ramon@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    [Fact]
    public async Task AssignedToSlh_ReturnsCollection()
    {
        var collector = CollectorWith(FacilityCode.SLH);
        var (handler, slaughterRepo) = Build(collector, collector.Id);
        slaughterRepo.Setup(r => r.GetMobileSlaughterCollectionAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileSlaughterCollectionDto(new DateOnly(2026, 6, 9), 0, 0, 0m, 250m, 365m,
                Array.Empty<SlaughterTransactionDto>(), Array.Empty<string>()));

        var result = await handler.Handle(new GetMobileSlaughterCollectionQuery(2026, 6, 9), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(250m, result.Value!.HogRatePerHead);
    }

    [Fact]
    public async Task NotAssignedToSlh_ReturnsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, _) = Build(collector, collector.Id);

        var result = await handler.Handle(new GetMobileSlaughterCollectionQuery(2026, 6, 9), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NonCollectorUser_ReturnsForbidden()
    {
        var (handler, _) = Build(collector: null, collectorId: null);

        var result = await handler.Handle(new GetMobileSlaughterCollectionQuery(2026, 6, 9), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
