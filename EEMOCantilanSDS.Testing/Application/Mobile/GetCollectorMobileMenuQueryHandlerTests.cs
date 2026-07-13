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
    public async Task ReturnsOnlyTheMunicipalitysFacilities_WithRealNames_AssignmentAndAvailabilityFlagged()
    {
        var collector = NewCollector();
        var repo = new Mock<ICollectorRepository>();
        var facilityRepo = new Mock<IFacilityRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        repo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);

        // The municipality's ACTUAL facilities (tenant-scoped) — includes a custom facility with a real
        // name and intentionally omits the other Custom slots and facilities the LGU doesn't operate.
        var names = new Dictionary<FacilityCode, string>
        {
            [FacilityCode.NPM] = "Public Market",
            [FacilityCode.TCC] = "Tampak Commercial Center",
            [FacilityCode.NCC] = "New Commercial Center",
            [FacilityCode.SLH] = "Slaughterhouse",
            [FacilityCode.Custom1] = "Fishery Economics",
        };
        facilityRepo.Setup(r => r.GetFacilityNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<FacilityCode, string>)names);
        currentUser.SetupGet(u => u.CollectorId).Returns(collector.Id);
        var handler = new GetCollectorMobileMenuQueryHandler(repo.Object, facilityRepo.Object, currentUser.Object);

        var result = await handler.Handle(new GetCollectorMobileMenuQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Juan Collector", result.Value!.CollectorName);

        // Only the municipality's own facilities are returned — no full-enum noise (Custom2–5, unconfigured).
        Assert.Equal(names.Count, result.Value.Facilities.Count);
        Assert.DoesNotContain(result.Value.Facilities, f => f.Code == FacilityCode.Custom2);
        Assert.DoesNotContain(result.Value.Facilities, f => f.Code == FacilityCode.TPM);

        // A custom facility shows its REAL name, never "Custom1".
        var custom = result.Value.Facilities.Single(f => f.Code == FacilityCode.Custom1);
        Assert.Equal("Fishery Economics", custom.Name);

        // Assignment drives the lock: NPM + TCC assigned, the rest locked.
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.NPM).IsAssigned);
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.TCC).IsAssigned);
        Assert.False(result.Value.Facilities.Single(f => f.Code == FacilityCode.SLH).IsAssigned);
        Assert.False(custom.IsAssigned);

        // Availability additionally requires a built mobile screen — assigned NPM + TCC are available.
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.NPM).IsAvailable);
        Assert.True(result.Value.Facilities.Single(f => f.Code == FacilityCode.TCC).IsAvailable);
        Assert.False(result.Value.Facilities.Single(f => f.Code == FacilityCode.SLH).IsAvailable);
    }

    [Fact]
    public async Task NonCollectorUser_ReturnsForbidden()
    {
        var handler = new GetCollectorMobileMenuQueryHandler(
            Mock.Of<ICollectorRepository>(),
            Mock.Of<IFacilityRepository>(),
            Mock.Of<ICurrentUserService>());

        var result = await handler.Handle(new GetCollectorMobileMenuQuery(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }
}
