using EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;
using EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Testing.Support;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Admins;

/// <summary>
/// Peer-Head protection. Account management is Head-only, but a municipality can have several Heads — and a
/// Head must not be able to edit, reset or disable a PEER Head's account (that would let peers seize or lock
/// each other out). A Head keeps full control of their OWN account and of ordinary Admin accounts.
/// Enforced server-side, so hiding the buttons is never the only protection.
/// </summary>
public class AdminPeerHeadGuardTests
{
    private static readonly IEmailVerificationSender NoOpVerificationSender = Mock.Of<IEmailVerificationSender>();

    private static (Mock<IAdminRepository> repo, Mock<ICurrentUserService> user, Mock<IUnitOfWork> uow) Mocks(AdminUser admin, Guid? actingId)
    {
        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        repo.Setup(r => r.IsUsernameUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        repo.Setup(r => r.CountOtherActiveSuperAdminsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("acting.head");
        user.SetupGet(c => c.UserId).Returns(actingId);

        return (repo, user, new Mock<IUnitOfWork>());
    }

    private static UpdateAdminCommandHandler UpdateHandler(Mock<IAdminRepository> repo, Mock<ICurrentUserService> user, Mock<IUnitOfWork> uow) =>
        new(repo.Object, user.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, NoOpVerificationSender);

    // ── The guard itself ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Guard_DeniesPeerHead_AllowsSelfAndPlainAdmins()
    {
        var head = AdminUser.Create("Head", "head", "head@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        var plainAdmin = AdminUser.Create("Admin", "admin", "admin@eemo.gov", "Secret123!", AdminRole.Admin);

        Assert.False(AdminManagementGuard.CanActOn(head, Guid.NewGuid()));   // a different Head
        Assert.True(AdminManagementGuard.CanActOn(head, head.Id));           // own account
        Assert.True(AdminManagementGuard.CanActOn(plainAdmin, Guid.NewGuid())); // ordinary Admin

        // Fail closed: an unknown acting identity is not treated as the owner.
        Assert.False(AdminManagementGuard.CanActOn(head, null));
    }

    // ── Update ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_PeerHead_IsDenied_AndNothingSaved()
    {
        var target = AdminUser.Create("Other Head", "other.head", "other@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        var (repo, user, uow) = Mocks(target, actingId: Guid.NewGuid());

        var result = await UpdateHandler(repo, user, uow).Handle(
            new UpdateAdminCommand(target.Id, "Hijacked", "other.head", "attacker@evil.test", AdminRole.Admin),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Other Head", target.FullName);                 // untouched
        Assert.Equal("other@eemo.gov", target.Email);
        Assert.Equal(AdminRole.SuperAdmin, target.Role);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_OwnHeadAccount_IsAllowed()
    {
        var target = AdminUser.Create("Head", "head", "head@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        var (repo, user, uow) = Mocks(target, actingId: target.Id);

        var result = await UpdateHandler(repo, user, uow).Handle(
            new UpdateAdminCommand(target.Id, "Head Renamed", "head", "head@eemo.gov", AdminRole.SuperAdmin),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Head Renamed", target.FullName);
    }

    [Fact]
    public async Task Update_PlainAdmin_IsAllowedByAnyHead()
    {
        var target = AdminUser.Create("Staff", "staff", "staff@eemo.gov", "Secret123!", AdminRole.Admin);
        var (repo, user, uow) = Mocks(target, actingId: Guid.NewGuid());

        var result = await UpdateHandler(repo, user, uow).Handle(
            new UpdateAdminCommand(target.Id, "Staff Renamed", "staff", "staff@eemo.gov", AdminRole.Admin),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Staff Renamed", target.FullName);
    }

    // ── Password reset ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_PeerHead_IsDenied_AndHashUnchanged()
    {
        var actingHead = AdminUser.Create("Acting", "acting.head", "acting@eemo.gov", "ActingPass1!", AdminRole.SuperAdmin);
        var target = AdminUser.Create("Other Head", "other.head", "other@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        var originalHash = target.PasswordHash;

        var repo = new Mock<IAdminRepository>();
        // The acting user re-authenticates as themselves; the target is the peer Head.
        repo.Setup(r => r.GetByIdAsync(actingHead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(actingHead);
        repo.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.UserId).Returns(actingHead.Id);
        user.SetupGet(c => c.Username).Returns("acting.head");
        var uow = new Mock<IUnitOfWork>();

        var result = await new ResetAdminPasswordCommandHandler(repo.Object, user.Object, uow.Object)
            .Handle(new ResetAdminPasswordCommand(target.Id, "BrandNew123!", "ActingPass1!"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(originalHash, target.PasswordHash);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
