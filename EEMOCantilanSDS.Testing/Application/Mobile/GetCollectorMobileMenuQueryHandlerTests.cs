using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetCollectorMobileMenuQueryHandlerTests
{
    private static CollectorUser NewCollector()
    {
        var collector = CollectorUser.Create("Juan Collector", "EEMO-2026-001", "juan", "juan@eemo.gov", "09170000000", "Secret123!");
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), FacilityCode.NPM));
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), FacilityCode.TCC));
        return collector;
    }

    [Fact]
    public async Task CollectorWithAssignments_ReturnsAllFacilities_AssignedFlaggedAndOnlyNpmAvailable()
    {
        var collector = NewCollector();
        var repo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        repo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(u => u.CollectorId).Returns(collector.Id);
        var handler = new GetCollectorMobileMenuQueryHandler(repo.Object, currentUser.Object);

        var result = await handler.Handle(new GetCollectorMobileMenuQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Juan Collector", result.Value!.CollectorName);

        // Every facility is returned so the menu can show locked (unassigned) ones.
        var allCodes = Enum.GetValues<FacilityCode>();
        Assert.Equal(allCodes.Length, result.Value.Facilities.Count);

        // Assignment drives the lock: NPM + TCC assigned, the rest locked.
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.NPM).IsAssigned);
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.TCC).IsAssigned);
        Assert.False(result.Value.Facilities.Single(f => f.Code == FacilityCode.SLH).IsAssigned);

        // Availability additionally requires a built mobile screen — NPM + the monthly-rental group.
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.NPM).IsAvailable);
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.TCC).IsAvailable);
        Assert.False(result.Value.Facilities.Single(f => f.Code == FacilityCode.SLH).IsAvailable);
    }

    [Fact]
    public async Task NonCollectorUser_ReturnsForbidden()
    {
        var handler = new GetCollectorMobileMenuQueryHandler(
            Mock.Of<ICollectorRepository>(),
            Mock.Of<ICurrentUserService>());

        var result = await handler.Handle(new GetCollectorMobileMenuQuery(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
