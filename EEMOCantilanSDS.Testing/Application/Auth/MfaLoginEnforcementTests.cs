using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// MFA sign-in enforcement (Slice 2).
/// <para>
/// The rules pinned here are the whole point of the feature: an MFA-enabled account gets NO tokens from the
/// password step, tokens are only minted once a valid authenticator (or recovery) code is supplied, the
/// challenge is single-use and expiring, wrong codes feed the lockout, and accounts WITHOUT MFA keep the
/// exact previous behaviour.
/// </para>
/// </summary>
public class MfaLoginEnforcementTests
{
    private const string Password = "Secret123!";

    private sealed class FakeProtector : ICredentialProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : ciphertext;
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>An MFA-enabled admin with a known secret and one recovery code.</summary>
    private static AdminUser EnrolledAdmin(out string secret, out string recoveryCode)
    {
        var admin = AdminUser.Create("Head", "head", "head@eemo.gov", Password, AdminRole.SuperAdmin, Guid.NewGuid());
        secret = new TotpService().GenerateSecret();
        var (plain, hashes) = RecoveryCodes.Generate(1);
        recoveryCode = plain[0];
        admin.BeginMfaEnrollment("enc:" + secret);
        admin.ConfirmMfaEnrollment(0, hashes);          // step 0 so current codes are still accepted
        return admin;
    }

    private static string CodeFor(string secret, long offset = 0)
    {
        var totp = new TotpService();
        return TotpService.ComputeCode(TotpService.FromBase32(secret), totp.CurrentStep() + offset);
    }

    // ── Password step ───────────────────────────────────────────────────────────────────────────

    private static LoginCommandHandler LoginHandler(AdminUser user, Mock<ITokenService> token, Mock<IUnitOfWork> uow)
    {
        var repo = new Mock<IAuthRepository>();
        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        token.Setup(t => t.CreateTokenResponse(It.IsAny<BaseUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "at", RefreshToken = "rt" });
        return new LoginCommandHandler(repo.Object, Mock.Of<IMunicipalityRepository>(), token.Object, uow.Object, new IdentityPasswordHasher());
    }

    [Fact]
    public async Task Login_MfaEnabled_IssuesChallenge_AndNoTokens()
    {
        var admin = EnrolledAdmin(out _, out _);
        var token = new Mock<ITokenService>();
        var handler = LoginHandler(admin, token, new Mock<IUnitOfWork>());

        var result = await handler.Handle(new LoginCommand("head", Password, null), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.MfaRequired);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.MfaChallengeToken));
        // The critical assertion: NO session was created by the password step.
        Assert.Equal(string.Empty, result.Value.AccessToken);
        Assert.Equal(string.Empty, result.Value.RefreshToken);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<BaseUser>()), Times.Never);
        // Only the hash is persisted; the raw challenge exists solely in the response.
        Assert.NotNull(admin.MfaChallengeTokenHash);
        Assert.NotEqual(result.Value.MfaChallengeToken, admin.MfaChallengeTokenHash);
    }

    [Fact]
    public async Task Login_WithoutMfa_IssuesTokensExactlyAsBefore()
    {
        var admin = AdminUser.Create("Plain", "plain", "plain@eemo.gov", Password, AdminRole.Admin, Guid.NewGuid());
        var token = new Mock<ITokenService>();
        var handler = LoginHandler(admin, token, new Mock<IUnitOfWork>());

        var result = await handler.Handle(new LoginCommand("plain", Password, null), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.MfaRequired);
        Assert.Null(result.Value.MfaChallengeToken);
        Assert.Equal("at", result.Value.AccessToken);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<BaseUser>()), Times.Once);
    }

    [Fact]
    public async Task Login_WrongPassword_OnMfaAccount_IssuesNoChallenge()
    {
        var admin = EnrolledAdmin(out _, out _);
        var handler = LoginHandler(admin, new Mock<ITokenService>(), new Mock<IUnitOfWork>());

        var result = await handler.Handle(new LoginCommand("head", "WrongPass1!", null), default);

        Assert.False(result.IsSuccess);
        Assert.Null(admin.MfaChallengeTokenHash);
    }

    // ── Verify step ─────────────────────────────────────────────────────────────────────────────

    private static async Task<(VerifyMfaLoginCommandHandler handler, AppDbContext ctx, Mock<ITokenService> token)>
        VerifyHandlerAsync(DbContextOptions<AppDbContext> options, AdminUser admin, string challenge)
    {
        using (var seed = new AppDbContext(options))
        {
            admin.SetMfaChallenge(Hash(challenge), DateTime.UtcNow.AddMinutes(5));
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
        }

        var ctx = new AppDbContext(options);
        var token = new Mock<ITokenService>();
        token.Setup(t => t.CreateTokenResponse(It.IsAny<BaseUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "at", RefreshToken = "rt" });

        return (new VerifyMfaLoginCommandHandler(ctx, new FakeProtector(), new TotpService(), token.Object), ctx, token);
    }

    [Fact]
    public async Task Verify_ValidChallengeAndCode_IssuesSession_AndConsumesChallenge()
    {
        var options = Options();
        var admin = EnrolledAdmin(out var secret, out _);
        var (handler, ctx, token) = await VerifyHandlerAsync(options, admin, "chal-1");
        using (ctx)
        {
            var result = await handler.Handle(new VerifyMfaLoginCommand("chal-1", CodeFor(secret)), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("at", result.Value!.AccessToken);
            token.Verify(t => t.CreateTokenResponse(It.IsAny<BaseUser>()), Times.Once);
        }

        using var verify = new AppDbContext(options);
        var saved = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == admin.Id);
        Assert.Null(saved.MfaChallengeTokenHash);       // single use
    }

    [Fact]
    public async Task Verify_ChallengeCannotBeReused()
    {
        var options = Options();
        var admin = EnrolledAdmin(out var secret, out _);
        var (handler, ctx, _) = await VerifyHandlerAsync(options, admin, "chal-2");
        using (ctx)
        {
            Assert.True((await handler.Handle(new VerifyMfaLoginCommand("chal-2", CodeFor(secret)), default)).IsSuccess);
        }

        using var ctx2 = new AppDbContext(options);
        var handler2 = new VerifyMfaLoginCommandHandler(ctx2, new FakeProtector(), new TotpService(), Mock.Of<ITokenService>());
        var second = await handler2.Handle(new VerifyMfaLoginCommand("chal-2", CodeFor(secret, 1)), default);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task Verify_ExpiredChallenge_IsRejected()
    {
        var options = Options();
        var admin = EnrolledAdmin(out var secret, out _);
        using (var seed = new AppDbContext(options))
        {
            admin.SetMfaChallenge(Hash("chal-3"), DateTime.UtcNow.AddMinutes(-1));   // already expired
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options);
        var handler = new VerifyMfaLoginCommandHandler(ctx, new FakeProtector(), new TotpService(), Mock.Of<ITokenService>());

        var result = await handler.Handle(new VerifyMfaLoginCommand("chal-3", CodeFor(secret)), default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Verify_WrongCode_CountsAsFailedLogin_AndIssuesNoSession()
    {
        var options = Options();
        var admin = EnrolledAdmin(out _, out _);
        var (handler, ctx, token) = await VerifyHandlerAsync(options, admin, "chal-4");
        using (ctx)
        {
            var result = await handler.Handle(new VerifyMfaLoginCommand("chal-4", "000000"), default);

            Assert.False(result.IsSuccess);
            token.Verify(t => t.CreateTokenResponse(It.IsAny<BaseUser>()), Times.Never);
        }

        using var verify = new AppDbContext(options);
        var saved = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == admin.Id);
        Assert.Equal(1, saved.FailedAttempts);                  // feeds the existing lockout
        Assert.NotNull(saved.MfaChallengeTokenHash);            // challenge survives so the user can retry
    }

    [Fact]
    public async Task Verify_AcceptsRecoveryCode_AndSpendsIt()
    {
        var options = Options();
        var admin = EnrolledAdmin(out _, out var recoveryCode);
        var (handler, ctx, _) = await VerifyHandlerAsync(options, admin, "chal-5");
        using (ctx)
        {
            Assert.True((await handler.Handle(new VerifyMfaLoginCommand("chal-5", recoveryCode), default)).IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var saved = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == admin.Id);
        Assert.Equal(0, saved.MfaRecoveryCodesRemaining);       // single use
    }

    [Fact]
    public async Task Verify_UnknownChallenge_IsRejected()
    {
        var options = Options();
        var admin = EnrolledAdmin(out var secret, out _);
        var (handler, ctx, _) = await VerifyHandlerAsync(options, admin, "chal-6");
        using (ctx)
        {
            var result = await handler.Handle(new VerifyMfaLoginCommand("not-the-challenge", CodeFor(secret)), default);
            Assert.False(result.IsSuccess);
        }
    }

    /// <summary>An account deactivated between the two steps must not be able to finish signing in.</summary>
    [Fact]
    public async Task Verify_AccountDeactivatedBetweenSteps_IsRejected()
    {
        var options = Options();
        var admin = EnrolledAdmin(out var secret, out _);
        admin.Deactivate("tester");
        var (handler, ctx, token) = await VerifyHandlerAsync(options, admin, "chal-7");
        using (ctx)
        {
            var result = await handler.Handle(new VerifyMfaLoginCommand("chal-7", CodeFor(secret)), default);

            Assert.False(result.IsSuccess);
            token.Verify(t => t.CreateTokenResponse(It.IsAny<BaseUser>()), Times.Never);
        }
    }

    /// <summary>
    /// Disabling MFA must also drop any outstanding challenge, so a captured challenge cannot be redeemed
    /// after the second factor is removed.
    /// </summary>
    [Fact]
    public void DisableMfa_ClearsOutstandingChallenge()
    {
        var admin = EnrolledAdmin(out _, out _);
        admin.SetMfaChallenge(Hash("chal-8"), DateTime.UtcNow.AddMinutes(5));

        admin.DisableMfa();

        Assert.Null(admin.MfaChallengeTokenHash);
        Assert.False(admin.IsMfaChallengeValid(Hash("chal-8")));
    }
}
