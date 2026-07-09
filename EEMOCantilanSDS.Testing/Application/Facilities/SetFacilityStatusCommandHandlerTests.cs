using EEMOCantilanSDS.Application.Command.Facilities.SetFacilityStatus;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class SetFacilityStatusCommandHandlerTests
{
    private static SetFacilityStatusCommandHandler Build(Mock<IFacilityRepository> repo, Mock<IUnitOfWork> uow)
        => new(repo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

    [Fact]
    public async Task Deactivate_SetsFacilityInactive()
    {
        var facility = Facility.Create(FacilityCode.ICE, "Iceplant", "ICE"); // active by default
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(FacilityCode.ICE, It.IsAny<CancellationToken>())).ReturnsAsync(facility);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow).Handle(new SetFacilityStatusCommand("ICE", false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(facility.IsActive);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reactivate_SetsFacilityActive()
    {
        var facility = Facility.Create(FacilityCode.ICE, "Iceplant", "ICE");
        facility.Deactivate();
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(FacilityCode.ICE, It.IsAny<CancellationToken>())).ReturnsAsync(facility);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow).Handle(new SetFacilityStatusCommand("ICE", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(facility.IsActive);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenNotConfigured()
    {
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>())).ReturnsAsync((Facility?)null);
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow).Handle(new SetFacilityStatusCommand("BBQ", false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rejects_UnknownCode()
    {
        var repo = new Mock<IFacilityRepository>();
        var uow = new Mock<IUnitOfWork>();

        var result = await Build(repo, uow).Handle(new SetFacilityStatusCommand("ZZZ", false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
