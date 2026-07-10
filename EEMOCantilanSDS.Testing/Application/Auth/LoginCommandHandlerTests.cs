using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class LoginCommandHandlerTests
{
    private const string Password = "Secret123!";

    private static AdminUser NewAdmin() =>
        AdminUser.Create("Head Admin", "head", "head@eemo.gov", Password, AdminRole.SuperAdmin);

    private static (LoginCommandHandler handler, Mock<ITokenService> token, Mock<IUnitOfWork> uow, Mock<IMunicipalityRepository> muni) Build(AdminUser? user)
    {
        var repo = new Mock<IAuthRepository>();
        var muni = new Mock<IMunicipalityRepository>();
        var token = new Mock<ITokenService>();
        var uow = new Mock<IUnitOfWork>();

        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        // Scoped login (?lgu={code}) resolves the tenant first and uses the tenant-scoped overload; stub it
        // to the same account so the per-municipality boundary check is what these tests exercise.
        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        token.Setup(t => t.CreateTokenResponse(It.IsAny<AdminUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "at", RefreshToken = "rt" });

        return (new LoginCommandHandler(repo.Object, muni.Object, token.Object, uow.Object), token, uow, muni);
    }

    [Fact]
    public async Task ValidActiveAdmin_ReturnsToken()
    {
        var (handler, _, _, _) = Build(NewAdmin());

        // No municipality code → today's behaviour, no boundary.
        var result = await handler.Handle(new LoginCommand("head", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at", result.Value!.AccessToken);
    }

    [Fact]
    public async Task BadPassword_RecordsFailedAttempt_AndReturnsUnauthorized()
    {
        var admin = NewAdmin();
        var (handler, token, uow, _) = Build(admin);

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
        var (handler, token, _, _) = Build(admin);

        var result = await handler.Handle(new LoginCommand("head", Password), CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }

    [Fact]
    public async Task InactiveAdmin_ReturnsForbidden()
    {
        var admin = NewAdmin();
        admin.Deactivate("test");
        var (handler, token, _, _) = Build(admin);

        var result = await handler.Handle(new LoginCommand("head", Password), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }

    // ── Per-municipality login boundary (scoped login ?lgu={code}) ──────────────────────────────────

    [Fact]
    public async Task ScopedLogin_AccountBelongsToThatMunicipality_ReturnsToken()
    {
        var lgu = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active);
        var admin = AdminUser.Create("Head Admin", "head", "head@eemo.gov", Password, AdminRole.SuperAdmin, lgu.Id);
        var (handler, token, _, muni) = Build(admin);
        muni.Setup(m => m.GetByIdentifierAsync("cantilan", It.IsAny<CancellationToken>())).ReturnsAsync(lgu);

        var result = await handler.Handle(new LoginCommand("head", Password, "cantilan"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Once);
    }

    [Fact]
    public async Task ScopedLogin_AccountBelongsToAnotherMunicipality_ReturnsForbidden_WithoutToken()
    {
        // The login page is scoped to Carrascal, but this (correctly-authenticated) account belongs elsewhere.
        var carrascal = Municipality.Create("CARRASCAL", "Carrascal", "Surigao del Sur", MunicipalityStatus.Active);
        var admin = AdminUser.Create("Head Admin", "head", "head@eemo.gov", Password, AdminRole.SuperAdmin, Guid.NewGuid());
        var (handler, token, _, muni) = Build(admin);
        muni.Setup(m => m.GetByIdentifierAsync("carrascal", It.IsAny<CancellationToken>())).ReturnsAsync(carrascal);

        var result = await handler.Handle(new LoginCommand("head", Password, "carrascal"), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }

    [Fact]
    public async Task ScopedLogin_UnknownMunicipalityCode_ReturnsForbidden_WithoutToken()
    {
        var admin = NewAdmin();
        var (handler, token, _, muni) = Build(admin);
        muni.Setup(m => m.GetByIdentifierAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Municipality?)null);

        var result = await handler.Handle(new LoginCommand("head", Password, "does-not-exist"), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }
}
