using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The signed-in administrator replaces their own password.
///
/// <para>
/// This is the only way out of a required change, so the tests care about two things above all: that succeeding actually
/// CLEARS the requirement — otherwise the user is asked again for ever — and that it still proves who is asking, because an
/// office-issued password may have been written on paper and handed over.
/// </para>
/// </summary>
public class ChangeMyPasswordCommandHandlerTests
{
    private const string Issued = "Issued-Passw0rd!";
    private const string Chosen = "Chosen-Passw0rd!";

    private static (ChangeMyPasswordCommandHandler handler, AdminUser admin, Mock<IUnitOfWork> uow) Build()
    {
        var hasher = new IdentityPasswordHasher();

        // As the office issues it: created with a password the office knows, so a change is required.
        var admin = AdminUser.Create("Office Head", "head", "head@example.gov.ph", hasher.Hash(Issued), AdminRole.SuperAdmin);

        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(admin.Id);

        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.CreateTokenResponse(It.IsAny<BaseUser>()))
              .ReturnsAsync(new TokenResponseDto { AccessToken = "new-access", RefreshToken = "new-refresh" });

        var uow = new Mock<IUnitOfWork>();

        return (new ChangeMyPasswordCommandHandler(repo.Object, currentUser.Object, tokens.Object, uow.Object, hasher), admin, uow);
    }

    [Fact]
    public async Task ChangingThePasswordClearsTheRequirement()
    {
        var (handler, admin, uow) = Build();
        Assert.True(admin.MustChangePassword, "the office-issued account should start out requiring a change");

        var result = await handler.Handle(new ChangeMyPasswordCommand(Issued, Chosen), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(admin.MustChangePassword);
        Assert.Equal("new-access", result.Value!.AccessToken);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheNewPasswordWorksAndTheIssuedOneStopsWorking()
    {
        var (handler, admin, _) = Build();
        var hasher = new IdentityPasswordHasher();

        await handler.Handle(new ChangeMyPasswordCommand(Issued, Chosen), CancellationToken.None);

        Assert.Equal(PasswordCheck.Succeeded, hasher.Check(admin.PasswordHash, Chosen));
        Assert.Equal(PasswordCheck.Failed, hasher.Check(admin.PasswordHash, Issued));
    }

    [Fact]
    public async Task TheWrongCurrentPasswordIsRefused()
    {
        // Re-authentication is not skipped just because the change is required: the person at the keyboard is not proven to
        // be the person the password was issued to until they can produce it.
        var (handler, admin, uow) = Build();

        var result = await handler.Handle(new ChangeMyPasswordCommand("not-the-issued-one", Chosen), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.True(admin.MustChangePassword, "a refused attempt must leave the requirement in place");
    }

    [Fact]
    public async Task ReusingTheSamePasswordIsRefused()
    {
        // Otherwise the requirement is satisfied while the account still holds the password the office knows — which is the
        // thing it exists to end.
        var (handler, admin, _) = Build();

        var result = await handler.Handle(new ChangeMyPasswordCommand(Issued, Issued), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(admin.MustChangePassword);
    }

    [Fact]
    public async Task OtherSessionsAreSignedOut()
    {
        // A credential change should not leave a live session on another device. The refresh token is revoked by the domain
        // and a fresh one issued for THIS session, in that order.
        var (handler, admin, _) = Build();
        admin.SetRefreshToken("old-refresh-hash", DateTime.UtcNow.AddDays(7));

        var result = await handler.Handle(new ChangeMyPasswordCommand(Issued, Chosen), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(admin.CanRefresh("old-refresh-hash", DateTime.UtcNow));
    }

    [Fact]
    public async Task AnUnknownCallerIsRefused()
    {
        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AdminUser?)null);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());

        var handler = new ChangeMyPasswordCommandHandler(
            repo.Object, currentUser.Object, Mock.Of<ITokenService>(), Mock.Of<IUnitOfWork>(), new IdentityPasswordHasher());

        var result = await handler.Handle(new ChangeMyPasswordCommand(Issued, Chosen), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }
}
