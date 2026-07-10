using EEMOCantilanSDS.Application.Command.Facilities.AddFacility;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class AddFacilityCommandHandlerTests
{
    private static AddFacilityCommandHandler Build(Mock<IFacilityRepository> repo, Mock<IUnitOfWork> uow)
        => new(repo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

    [Fact]
    public async Task AddsFacility_WhenNotAlreadyConfigured_WithHeadChosenName()
    {
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(FacilityCode.ICE, It.IsAny<CancellationToken>())).ReturnsAsync((Facility?)null);
        Facility? added = null;
        repo.Setup(r => r.AddFacilityAsync(It.IsAny<Facility>(), It.IsAny<CancellationToken>()))
            .Callback<Facility, CancellationToken>((f, _) => added = f).Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new AddFacilityCommand("ICE", "Cold Storage Plant", "COLD", "Municipal cold storage"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(FacilityCode.ICE, added!.Code);           // canonical code drives billing machinery
        Assert.Equal("Cold Storage Plant", added.Name);         // Head-chosen custom name
        Assert.Equal("COLD", added.ShortName);
        Assert.Equal(BillingArchetype.MonthlyRental, added.Archetype); // defaulted from the code
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddsCustomFacility_AsMonthlyRental_WithHeadName()
    {
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(FacilityCode.Custom1, It.IsAny<CancellationToken>())).ReturnsAsync((Facility?)null);
        Facility? added = null;
        repo.Setup(r => r.AddFacilityAsync(It.IsAny<Facility>(), It.IsAny<CancellationToken>()))
            .Callback<Facility, CancellationToken>((f, _) => added = f).Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new AddFacilityCommand("Custom1", "Night Market", "NGT", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(FacilityCode.Custom1, added!.Code);
        Assert.Equal("Night Market", added.Name);                       // Head-chosen name
        Assert.Equal(BillingArchetype.MonthlyRental, added.Archetype);  // custom bills as monthly rental
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_WhenCodeAlreadyConfigured()
    {
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(FacilityCode.SLH, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH"));
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new AddFacilityCommand("SLH", "Abattoir", "SLH", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        repo.Verify(r => r.AddFacilityAsync(It.IsAny<Facility>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_UnknownCode_WithoutTouchingRepository()
    {
        var repo = new Mock<IFacilityRepository>();
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow)
            .Handle(new AddFacilityCommand("XYZ", "Whatever", "XYZ", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
