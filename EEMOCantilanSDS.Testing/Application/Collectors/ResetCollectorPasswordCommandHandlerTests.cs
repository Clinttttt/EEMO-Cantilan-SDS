using EEMOCantilanSDS.Application.Command.Collectors.ResetCollectorPassword;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class ResetCollectorPasswordCommandHandlerTests
{
    private static CollectorUser NewCollector() =>
        CollectorUser.Create("Juan Collector", "EMP-1", "juan", "juan@eemo.gov", "0917", "Secret123!");

    private static (Mock<ICollectorRepository> repo, Mock<ICurrentUserService> user, Mock<IUnitOfWork> uow) Mocks(CollectorUser? collector)
    {
        var repo = new Mock<ICollectorRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("head");
        return (repo, user, new Mock<IUnitOfWork>());
    }

    [Fact]
    public async Task Reset_ChangesHash_AndSaves()
    {
        var collector = NewCollector();
        var originalHash = collector.PasswordHash;
        var (repo, user, uow) = Mocks(collector);
        var handler = new ResetCollectorPasswordCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ResetCollectorPasswordCommand(collector.Id, "BrandNew123!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(originalHash, collector.PasswordHash);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reset_NotFound_Returns404()
    {
        var (repo, user, uow) = Mocks(null);
        var handler = new ResetCollectorPasswordCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ResetCollectorPasswordCommand(Guid.NewGuid(), "BrandNew123!"), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
