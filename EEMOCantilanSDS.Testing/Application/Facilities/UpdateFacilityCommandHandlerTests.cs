using EEMOCantilanSDS.Application.Command.Facilities.UpdateFacility;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class UpdateFacilityCommandHandlerTests
{
    private static UpdateFacilityCommandHandler Build(Mock<IFacilityRepository> repo, Mock<IUnitOfWork> uow)
        => new(repo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

    [Fact]
    public async Task RenamesFacility_CorrectingAnOnboardingArtifact()
    {
        var facility = Facility.Create(FacilityCode.TCC, "Madrid Commercial Center", "TCC"); // wrong LGU name
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(FacilityCode.TCC, It.IsAny<CancellationToken>())).ReturnsAsync(facility);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new UpdateFacilityCommand("TCC", "Carrascal Commercial Center", "CCC", "Corrected"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Carrascal Commercial Center", facility.Name);
        Assert.Equal("CCC", facility.ShortName);
        Assert.Equal("Corrected", facility.Description);
        Assert.Equal(FacilityCode.TCC, facility.Code);                 // code immutable
        Assert.Equal(BillingArchetype.MonthlyRental, facility.Archetype); // archetype immutable
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenFacilityNotConfiguredForTenant()
    {
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>())).ReturnsAsync((Facility?)null);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new UpdateFacilityCommand("ICE", "X", "X", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_UnknownCode_WithoutTouchingRepository()
    {
        var repo = new Mock<IFacilityRepository>();
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new UpdateFacilityCommand("ZZZ", "X", "X", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
