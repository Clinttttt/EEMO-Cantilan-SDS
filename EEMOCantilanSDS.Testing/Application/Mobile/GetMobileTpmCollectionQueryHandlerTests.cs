using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTpmCollection;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetMobileTpmCollectionQueryHandlerTests
{
    private static (GetMobileTpmCollectionQueryHandler handler, Mock<ITpmRepository> tpmRepo) Build(
        CollectorUser? collector, Guid? collectorId)
    {
        var collectorRepo = new Mock<ICollectorRepository>();
        var tpmRepo = new Mock<ITpmRepository>();
        var suggestionRepo = new Mock<ISuggestionRepository>();
        var currentUser = new Mock<ICurrentUserService>();

        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(u => u.CollectorId).Returns(collectorId);

        tpmRepo.Setup(r => r.GetAllVendorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EEMOCantilanSDS.Domain.Entities.TaboanMarket.TpmVendor>());
        suggestionRepo.Setup(r => r.GetHiddenValuesAsync(It.IsAny<SuggestionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<string>)new HashSet<string>());

        return (new GetMobileTpmCollectionQueryHandler(collectorRepo.Object, tpmRepo.Object, suggestionRepo.Object, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.TpmMarketDay, currentUser.Object), tpmRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Nena", "EEMO-2026-007", "nena", "nena@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    [Fact]
    public async Task AssignedToTpm_ReturnsCollection_OnFridayMarketDate()
    {
        var collector = CollectorWith(FacilityCode.TPM);
        var (handler, tpmRepo) = Build(collector, collector.Id);
        tpmRepo.Setup(r => r.GetVendorAttendanceAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TpmVendorAttendanceDto>
            {
                new() { Id = Guid.NewGuid(), VendorName = "A", IsPaid = true, Fee = 100m },
                new() { Id = Guid.NewGuid(), VendorName = "B", IsPaid = false, Fee = 100m }
            });

        var result = await handler.Handle(new GetMobileTpmCollectionQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DayOfWeek.Friday, result.Value!.MarketDate.DayOfWeek);  // always resolves to a Friday
        Assert.Equal(100m, result.Value.VendorFee);
        Assert.Equal(2, result.Value.VendorCount);
        Assert.Equal(100m, result.Value.CollectedAmount);
    }

    [Fact]
    public async Task NotAssignedToTpm_ReturnsForbidden()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var (handler, _) = Build(collector, collector.Id);

        var result = await handler.Handle(new GetMobileTpmCollectionQuery(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NonCollectorUser_ReturnsForbidden()
    {
        var (handler, _) = Build(collector: null, collectorId: null);

        var result = await handler.Handle(new GetMobileTpmCollectionQuery(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
