using EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;
using EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;
using EEMOCantilanSDS.Application.Command.Admins.ToggleAdminStatus;
using EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class AdminCommandHandlerTests
{
    private static AdminUser NewAdmin(AdminRole role = AdminRole.Admin) =>
        AdminUser.Create("Old Name", "olduser", "old@eemo.gov", "Secret123!", role);

    private static (Mock<IAdminRepository> repo, Mock<ICurrentUserService> user, Mock<IUnitOfWork> uow) Mocks(AdminUser? admin)
    {
        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("head");
        return (repo, user, new Mock<IUnitOfWork>());
    }

    [Fact]
    public async Task Create_AddsAdminWithRole_AndSaves()
    {
        var repo = new Mock<IAdminRepository>();
        var uow = new Mock<IUnitOfWork>();
        AdminUser? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<AdminUser>(), It.IsAny<CancellationToken>()))
            .Callback<AdminUser, CancellationToken>((a, _) => added = a)
            .Returns(Task.CompletedTask);
        var handler = new CreateAdminCommandHandler(repo.Object, uow.Object);

        var result = await handler.Handle(
            new CreateAdminCommand("Maria Santos", "maria", "maria@eemo.gov", "Secret123!", AdminRole.Admin),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(AdminRole.Admin, added!.Role);
        Assert.Equal("maria", result.Value!.Username);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Toggle_NotFound_Returns404()
    {
        var (repo, user, uow) = Mocks(null);
        var handler = new ToggleAdminStatusCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ToggleAdminStatusCommand(Guid.NewGuid(), Activate: true), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Toggle_Deactivate_BlockedForSelf()
    {
        var admin = NewAdmin(AdminRole.SuperAdmin);
        var (repo, user, uow) = Mocks(admin);
        user.SetupGet(c => c.UserId).Returns(admin.Id); // acting on own account
        // Another active SuperAdmin exists, so only the self-guard should trip.
        repo.Setup(r => r.CountOtherActiveSuperAdminsAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new ToggleAdminStatusCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ToggleAdminStatusCommand(admin.Id, Activate: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.True(admin.IsActive);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Toggle_Deactivate_BlockedForLastSuperAdmin()
    {
        var admin = NewAdmin(AdminRole.SuperAdmin);
        var (repo, user, uow) = Mocks(admin);
        user.SetupGet(c => c.UserId).Returns(Guid.NewGuid()); // a different head is acting
        repo.Setup(r => r.CountOtherActiveSuperAdminsAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var handler = new ToggleAdminStatusCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ToggleAdminStatusCommand(admin.Id, Activate: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task Toggle_Deactivate_PlainAdmin_Succeeds()
    {
        var admin = NewAdmin(AdminRole.Admin);
        var (repo, user, uow) = Mocks(admin);
        user.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        var handler = new ToggleAdminStatusCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ToggleAdminStatusCommand(admin.Id, Activate: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(admin.IsActive);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_DemoteLastSuperAdmin_Blocked()
    {
        var admin = NewAdmin(AdminRole.SuperAdmin);
        var (repo, user, uow) = Mocks(admin);
        repo.Setup(r => r.CountOtherActiveSuperAdminsAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var handler = new UpdateAdminCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(
            new UpdateAdminCommand(admin.Id, "New Name", "new@eemo.gov", AdminRole.Admin),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AdminRole.SuperAdmin, admin.Role);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_DemoteWhenAnotherSuperAdminExists_Succeeds()
    {
        var admin = NewAdmin(AdminRole.SuperAdmin);
        var (repo, user, uow) = Mocks(admin);
        repo.Setup(r => r.CountOtherActiveSuperAdminsAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new UpdateAdminCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(
            new UpdateAdminCommand(admin.Id, "New Name", "new@eemo.gov", AdminRole.Admin),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminRole.Admin, admin.Role);
        Assert.Equal("New Name", admin.FullName);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_ChangesHash_AndSaves()
    {
        var admin = NewAdmin(AdminRole.Admin);
        var originalHash = admin.PasswordHash;
        var (repo, user, uow) = Mocks(admin);
        var handler = new ResetAdminPasswordCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ResetAdminPasswordCommand(admin.Id, "BrandNew123!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(originalHash, admin.PasswordHash);
        Assert.True(admin.MustChangePassword);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_NotFound_Returns404()
    {
        var (repo, user, uow) = Mocks(null);
        var handler = new ResetAdminPasswordCommandHandler(repo.Object, user.Object, uow.Object);

        var result = await handler.Handle(new ResetAdminPasswordCommand(Guid.NewGuid(), "BrandNew123!"), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
