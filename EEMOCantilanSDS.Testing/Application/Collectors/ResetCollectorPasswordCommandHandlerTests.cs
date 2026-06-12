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

    // The acting Head whose own password authorizes the reset.
    private static AdminUser NewHead() =>
        AdminUser.Create("Head", "head", "head@eemo.gov", "HeadPass123!", AdminRole.SuperAdmin);

    private static (Mock<ICollectorRepository> collectorRepo, Mock<IAdminRepository> adminRepo, Mock<ICurrentUserService> user, Mock<IUnitOfWork> uow)
        Mocks(CollectorUser? collector, AdminUser head)
    {
        var collectorRepo = new Mock<ICollectorRepository>();
        collectorRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(collector);

        var adminRepo = new Mock<IAdminRepository>();
        adminRepo.Setup(r => r.GetByIdAsync(head.Id, It.IsAny<CancellationToken>())).ReturnsAsync(head);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("head");
        user.SetupGet(c => c.UserId).Returns(head.Id);

        return (collectorRepo, adminRepo, user, new Mock<IUnitOfWork>());
    }

    [Fact]
    public async Task Reset_WithCorrectConfirmation_ChangesHash_AndSaves()
    {
        var collector = NewCollector();
        var head = NewHead();
        var originalHash = collector.PasswordHash;
        var (collectorRepo, adminRepo, user, uow) = Mocks(collector, head);
        var handler = new ResetCollectorPasswordCommandHandler(collectorRepo.Object, adminRepo.Object, user.Object, uow.Object);

        var result = await handler.Handle(
            new ResetCollectorPasswordCommand(collector.Id, "BrandNew123!", "HeadPass123!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(originalHash, collector.PasswordHash);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reset_WrongConfirmation_IsRejected_AndDoesNotSave()
    {
        var collector = NewCollector();
        var head = NewHead();
        var originalHash = collector.PasswordHash;
        var (collectorRepo, adminRepo, user, uow) = Mocks(collector, head);
        var handler = new ResetCollectorPasswordCommandHandler(collectorRepo.Object, adminRepo.Object, user.Object, uow.Object);

        var result = await handler.Handle(
            new ResetCollectorPasswordCommand(collector.Id, "BrandNew123!", "wrong"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(originalHash, collector.PasswordHash);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reset_NotFound_Returns404()
    {
        var head = NewHead();
        var (collectorRepo, adminRepo, user, uow) = Mocks(null, head);
        var handler = new ResetCollectorPasswordCommandHandler(collectorRepo.Object, adminRepo.Object, user.Object, uow.Object);

        var result = await handler.Handle(
            new ResetCollectorPasswordCommand(Guid.NewGuid(), "BrandNew123!", "HeadPass123!"), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
