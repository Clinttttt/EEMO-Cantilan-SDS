using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class LoginCommandHandlerTests
{
    private const string Password = "Secret123!";

    private static AdminUser NewAdmin() =>
        AdminUser.Create("Head Admin", "head", "head@eemo.gov", Password, AdminRole.SuperAdmin);

    private static (LoginCommandHandler handler, Mock<ITokenService> token, Mock<IUnitOfWork> uow) Build(AdminUser? user)
    {
        var repo = new Mock<IAuthRepository>();
        var token = new Mock<ITokenService>();
        var uow = new Mock<IUnitOfWork>();

        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        token.Setup(t => t.CreateTokenResponse(It.IsAny<AdminUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "at", RefreshToken = "rt" });

        return (new LoginCommandHandler(repo.Object, token.Object, uow.Object), token, uow);
    }

    [Fact]
    public async Task ValidActiveAdmin_ReturnsToken()
    {
        var (handler, _, _) = Build(NewAdmin());

        var result = await handler.Handle(new LoginCommand("head", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at", result.Value!.AccessToken);
    }

    [Fact]
    public async Task BadPassword_RecordsFailedAttempt_AndReturnsUnauthorized()
    {
        var admin = NewAdmin();
        var (handler, token, uow) = Build(admin);

        var result = await handler.Handle(new LoginCommand("head", "wrong"), CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        Assert.Equal(1, admin.FailedAttempts);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }

    [Fact]
    public async Task LockedOutAdmin_ReturnsUnauthorized_WithoutIssuingToken()
    {
        var admin = NewAdmin();
        for (var i = 0; i < 5; i++) admin.RecordFailedLogin();
        Assert.True(admin.IsLockedOut);
        var (handler, token, _) = Build(admin);

        var result = await handler.Handle(new LoginCommand("head", Password), CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }

    [Fact]
    public async Task InactiveAdmin_ReturnsForbidden()
    {
        var admin = NewAdmin();
        admin.Deactivate("test");
        var (handler, token, _) = Build(admin);

        var result = await handler.Handle(new LoginCommand("head", Password), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }
}
