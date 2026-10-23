using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Auth;

public class MultiMunicipalityPasswordResolutionTests
{
    private static AdminUser Account(Guid municipalityId, string password) =>
        AdminUser.Create("Office Head", "head", "head@office.gov", TestPasswords.Hash(password),
            AdminRole.SuperAdmin, municipalityId, mustChangePassword: false);

    private static (LoginCommandHandler handler, Mock<ITokenService> tokens,
        Mock<IUnitOfWork> writes) Build(params AdminUser[] accounts)
    {
        var repo = new Mock<IAuthRepository>();
        repo.Setup(x => x.GetAdminsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(accounts);
        repo.Setup(x => x.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid mid, CancellationToken _) =>
                accounts.SingleOrDefault(a => a.MunicipalityId == mid));
        var offices = new Mock<IMunicipalityRepository>();
        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active);
        offices.Setup(x => x.GetByIdentifierAsync("cantilan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cantilan);
        var tokens = new Mock<ITokenService>();
        tokens.Setup(x => x.CreateTokenResponse(It.IsAny<AdminUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "access", RefreshToken = "refresh" });
        var writes = new Mock<IUnitOfWork>();
        return (new LoginCommandHandler(repo.Object, offices.Object, tokens.Object, writes.Object,
            new IdentityPasswordHasher(), new FixedClock(DateTime.UtcNow)), tokens, writes);
    }

    [Fact]
    public async Task SameUsername_DifferentPasswords_AuthenticatesExactlyMatchingMunicipality()
    {
        var cantilan = Account(Guid.NewGuid(), "Cantilan-Pass123!");
        var madrid = Account(Guid.NewGuid(), "Madrid-Pass123!");
        var (handler, tokens, _) = Build(cantilan, madrid);
        var result = await handler.Handle(new LoginCommand("head", "Madrid-Pass123!"), default);
        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value!.AccessToken);
        tokens.Verify(x => x.CreateTokenResponse(madrid), Times.Once);
        tokens.Verify(x => x.CreateTokenResponse(cantilan), Times.Never);
        Assert.Equal(0, cantilan.FailedAttempts);
    }

    [Fact]
    public async Task IdenticalCredentialsAcrossTwoOffices_NeverGuessOrIssueToken()
    {
        var a = Account(Guid.NewGuid(), "Shared-Pass123!");
        var b = Account(Guid.NewGuid(), "Shared-Pass123!");
        var (handler, tokens, writes) = Build(a, b);
        var result = await handler.Handle(new LoginCommand("head", "Shared-Pass123!"), default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("direct sign-in link", result.Error);
        tokens.Verify(x => x.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
        writes.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidPasswordWithDuplicateUsername_IsUnauthorizedAndCounted()
    {
        var a = Account(Guid.NewGuid(), "First-Pass123!");
        var b = Account(Guid.NewGuid(), "Second-Pass123!");
        var (handler, tokens, writes) = Build(a, b);
        var result = await handler.Handle(new LoginCommand("head", "wrong"), default);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Equal(1, a.FailedAttempts);
        Assert.Equal(1, b.FailedAttempts);
        writes.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(x => x.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }

    [Fact]
    public async Task CorrectPasswordOfLockedAccount_DoesNotFallThroughToOtherLGU()
    {
        var locked = Account(Guid.NewGuid(), "Locked-Pass123!");
        for (var i = 0; i < 5; i++) locked.RecordFailedLogin(DateTime.UtcNow);
        var other = Account(Guid.NewGuid(), "Other-Pass123!");
        var (handler, tokens, _) = Build(other, locked);
        var result = await handler.Handle(new LoginCommand("head", "Locked-Pass123!"), default);
        Assert.Equal(ResultStatus.Locked, result.Status);
        tokens.Verify(x => x.CreateTokenResponse(It.IsAny<AdminUser>()), Times.Never);
    }
}
