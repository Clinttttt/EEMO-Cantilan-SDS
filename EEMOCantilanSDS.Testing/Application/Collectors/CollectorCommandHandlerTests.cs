using EEMOCantilanSDS.Application.Command.Collectors.ToggleCollectorStatus;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class CollectorCommandHandlerTests
{
    private static CollectorUser NewCollector() =>
        CollectorUser.Create("Old Name", "EMP-1", "juan", "old@eemo.gov", "0917", "Secret123!");

    private static (Mock<ICollectorRepository> repo, Mock<ICurrentUserService> user, Mock<IUnitOfWork> uow) Mocks(CollectorUser? collector)
    {
        var repo = new Mock<ICollectorRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("head");
        return (repo, user, new Mock<IUnitOfWork>());
    }

    [Fact]
    public async Task Toggle_Deactivate_SetsInactive_AndSaves()
    {
        var collector = NewCollector(); // starts active
        var (repo, user, uow) = Mocks(collector);
        var handler = new ToggleCollectorStatusCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ToggleCollectorStatusCommand(collector.Id, Activate: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(collector.IsActive);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Toggle_NotFound_Returns404()
    {
        var (repo, user, uow) = Mocks(null);
        var handler = new ToggleCollectorStatusCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ToggleCollectorStatusCommand(Guid.NewGuid(), Activate: true), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesProfile_AndReplacesFacilities()
    {
        var collector = NewCollector();
        var (repo, user, uow) = Mocks(collector);
        List<FacilityCode>? replacedWith = null;
        repo.Setup(r => r.ReplaceFacilityAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<List<FacilityCode>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, List<FacilityCode>, CancellationToken>((_, codes, _) => replacedWith = codes)
            .Returns(Task.CompletedTask);
        var handler = new UpdateCollectorCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(
            new UpdateCollectorCommand(collector.Id, "New Name", "0999", "new@eemo.gov", new() { FacilityCode.NPM, FacilityCode.TCC }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", collector.FullName);
        Assert.Equal("new@eemo.gov", collector.Email);
        Assert.Equal(new[] { FacilityCode.NPM, FacilityCode.TCC }, replacedWith);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
