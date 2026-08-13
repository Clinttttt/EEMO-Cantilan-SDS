using EEMOCantilanSDS.Infrastructure.Security;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// Self-service password reset (OWASP "Forgot Password"): a one-time, hashed, expiring token emailed to a
/// VERIFIED address. These tests pin the security-critical behaviour:
/// enumeration-safety (identical neutral response for every input), per-LGU isolation, the verified-email
/// requirement, the per-account request throttle, single use, expiry, and that a reset can never re-enable a
/// deactivated account.
/// </summary>
public class PasswordResetHandlerTests
{
    private const string RawToken = "reset-token-abc";

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Records what was emailed so tests can assert an email was (or was not) sent.</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        public int SendCount { get; private set; }
        public string? LastTo { get; private set; }
        public string? LastBody { get; private set; }

        public Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, CancellationToken ct = default)
        {
            SendCount++;
            LastTo = toEmail;
            LastBody = body;
            return Task.FromResult(true);
        }
    }

    private static RequestPasswordResetCommandHandler RequestHandler(
        AppDbContext ctx, IEmailSender email, Municipality? resolved = null)
    {
        var muni = new Mock<IMunicipalityRepository>();
        muni.Setup(m => m.GetByIdentifierAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        return new RequestPasswordResetCommandHandler(ctx, muni.Object, email);
    }

    /// <summary>Seeds an active admin whose email is verified (reset-eligible).</summary>
    private static async Task<(Guid id, Guid municipalityId)> SeedVerifiedAdminAsync(
        DbContextOptions<AppDbContext> options,
        string username = "head",
        string email = "head@eemo.gov.ph",
        bool emailVerified = true,
        bool isActive = true)
    {
        using var seed = new AppDbContext(options);
        var municipalityId = Guid.NewGuid();
        var admin = AdminUser.Create("Head Admin", username, email, "OldPass123", AdminRole.SuperAdmin, municipalityId, isActive: isActive);
        if (emailVerified) admin.MarkEmailVerified();
        seed.AdminUsers.Add(admin);
        await seed.SaveChangesAsync();
        return (admin.Id, municipalityId);
    }

    // ── Request: happy path ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_VerifiedActiveAdmin_IssuesHashedToken_AndEmailsLink()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options);
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
        {
            var result = await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("head@eemo.gov.ph"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.False(string.IsNullOrEmpty(admin.PasswordResetTokenHash));
        Assert.NotNull(admin.PasswordResetTokenExpiry);
        Assert.True(admin.PasswordResetTokenExpiry > DateTime.UtcNow);
        Assert.NotNull(admin.PasswordResetRequestedAt);
        Assert.Equal(1, email.SendCount);
        Assert.Equal("head@eemo.gov.ph", email.LastTo);
        // The RAW token must never be persisted — only its hash.
        Assert.DoesNotContain(admin.PasswordResetTokenHash!, email.LastBody!);
    }

    [Fact]
    public async Task Request_EmailMatchIsCaseInsensitive()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options);
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
            await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("HEAD@eemo.gov.ph"), default);

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.False(string.IsNullOrEmpty(admin.PasswordResetTokenHash));
        Assert.Equal(1, email.SendCount);
    }

    /// <summary>
    /// Recovery is EMAIL-only: a username is not an accepted identifier, so it can never be used as a
    /// username→mailbox oracle. The response stays the same neutral success.
    /// </summary>
    [Fact]
    public async Task Request_UsernameIsNotAccepted_OnlyEmail()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options, username: "head", email: "head@eemo.gov.ph");
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
        {
            var result = await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("head"), default);
            Assert.True(result.IsSuccess);   // still neutral
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.Null(admin.PasswordResetTokenHash);   // no token issued for a username
        Assert.Equal(0, email.SendCount);            // and nothing emailed
    }

    /// <summary>
    /// Regression: email uniqueness is per-LGU, so the SAME address can be registered in several
    /// municipalities. An UNSCOPED request (plain /forgot-password, no ?lgu=) must not silently pick one
    /// arbitrary account — that reset a different LGU's account than the user meant, leaving their own
    /// password unchanged ("invalid username or password" on the next sign-in). Every eligible match now
    /// gets its own single-use link, each email naming its own LGU and username.
    /// </summary>
    [Fact]
    public async Task Request_Unscoped_SharedEmailAcrossLgus_IssuesALinkForEveryAccount()
    {
        var options = Options();
        Guid cantilanId, carmenId;

        using (var seed = new AppDbContext(options))
        {
            var cantilan = AdminUser.Create("Cantilan Head", "head2", "shared@lgu.gov.ph", "OldPass123", AdminRole.SuperAdmin, Guid.NewGuid());
            cantilan.MarkEmailVerified();
            var carmen = AdminUser.Create("Carmen Head", "carmen.head", "shared@lgu.gov.ph", "OldPass123", AdminRole.SuperAdmin, Guid.NewGuid());
            carmen.MarkEmailVerified();
            seed.AdminUsers.AddRange(cantilan, carmen);
            await seed.SaveChangesAsync();
            cantilanId = cantilan.Id;
            carmenId = carmen.Id;
        }

        var email = new RecordingEmailSender();
        using (var ctx = new AppDbContext(options))
        {
            // No municipality code — exactly the plain /forgot-password case.
            var result = await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("shared@lgu.gov.ph"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var a = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == cantilanId);
        var b = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == carmenId);

        // BOTH accounts get their own token — neither is silently skipped, and the tokens differ.
        Assert.False(string.IsNullOrEmpty(a.PasswordResetTokenHash));
        Assert.False(string.IsNullOrEmpty(b.PasswordResetTokenHash));
        Assert.NotEqual(a.PasswordResetTokenHash, b.PasswordResetTokenHash);
        Assert.Equal(2, email.SendCount);
    }

    // ── Request: enumeration-safety & eligibility ────────────────────────────────────────────────

    [Fact]
    public async Task Request_UnknownAccount_ReturnsSameNeutralSuccess_AndSendsNothing()
    {
        var options = Options();
        await SeedVerifiedAdminAsync(options);
        var email = new RecordingEmailSender();

        using var ctx = new AppDbContext(options);
        var result = await RequestHandler(ctx, email)
            .Handle(new RequestPasswordResetCommand("nobody@nowhere.gov.ph"), default);

        // Identical to the happy path — the caller cannot tell whether the account exists.
        Assert.True(result.IsSuccess);
        Assert.Equal(0, email.SendCount);
    }

    [Fact]
    public async Task Request_UnverifiedEmail_IsNotEligible_ButStillNeutral()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options, emailVerified: false);
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
        {
            var result = await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("head@eemo.gov.ph"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.Null(admin.PasswordResetTokenHash);
        Assert.Equal(0, email.SendCount);
    }

    [Fact]
    public async Task Request_InactiveAccount_IsNotEligible_ButStillNeutral()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options, isActive: false);
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
        {
            var result = await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("head@eemo.gov.ph"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.Null(admin.PasswordResetTokenHash);
        Assert.Equal(0, email.SendCount);
    }

    [Fact]
    public async Task Request_Throttled_SecondRequestWithinWindow_DoesNotReissueOrResend()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options);
        var email = new RecordingEmailSender();

        string? firstHash;
        using (var ctx = new AppDbContext(options))
            await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("head@eemo.gov.ph"), default);

        using (var read = new AppDbContext(options))
            firstHash = (await read.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id)).PasswordResetTokenHash;

        using (var ctx = new AppDbContext(options))
        {
            var result = await RequestHandler(ctx, email).Handle(new RequestPasswordResetCommand("head@eemo.gov.ph"), default);
            Assert.True(result.IsSuccess);   // still neutral
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.Equal(firstHash, admin.PasswordResetTokenHash);   // token not rotated
        Assert.Equal(1, email.SendCount);                        // and no second email
    }

    // ── Request: per-LGU isolation ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_ScopedToLgu_OnlyTouchesThatMunicipalitysAccount()
    {
        var options = Options();
        Guid targetId, otherId, targetMunicipalityId;

        // The SAME email address is registered in two LGUs (a shared office mailbox), so only the LGU code
        // can disambiguate — the scoped request must resolve exactly one account.
        using (var seed = new AppDbContext(options))
        {
            targetMunicipalityId = Guid.NewGuid();
            var target = AdminUser.Create("Carmen Head", "carmen.head", "shared@lgu.gov.ph", "OldPass123", AdminRole.SuperAdmin, targetMunicipalityId);
            target.MarkEmailVerified();
            var other = AdminUser.Create("Cantilan Head", "cantilan.head", "shared@lgu.gov.ph", "OldPass123", AdminRole.SuperAdmin, Guid.NewGuid());
            other.MarkEmailVerified();
            seed.AdminUsers.AddRange(target, other);
            await seed.SaveChangesAsync();
            targetId = target.Id;
            otherId = other.Id;
        }

        var email = new RecordingEmailSender();
        var carmen = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "carmen");
        // Force the resolved municipality id to the seeded tenant so scoping is exercised end-to-end.
        typeof(Municipality).GetProperty(nameof(Municipality.Id))!.SetValue(carmen, targetMunicipalityId);

        using (var ctx = new AppDbContext(options))
        {
            var result = await RequestHandler(ctx, email, carmen)
                .Handle(new RequestPasswordResetCommand("shared@lgu.gov.ph", "CARMEN"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var target2 = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == targetId);
        var other2 = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == otherId);
        Assert.False(string.IsNullOrEmpty(target2.PasswordResetTokenHash));   // the scoped LGU's account
        Assert.Null(other2.PasswordResetTokenHash);                           // the other LGU untouched
        Assert.Equal("shared@lgu.gov.ph", email.LastTo);
    }

    [Fact]
    public async Task Request_UnknownLguCode_IsNeutral_AndIssuesNothing()
    {
        var options = Options();
        var (id, _) = await SeedVerifiedAdminAsync(options);
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
        {
            // resolved: null => unknown code
            var result = await RequestHandler(ctx, email, resolved: null)
                .Handle(new RequestPasswordResetCommand("head@eemo.gov.ph", "NOPE"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        Assert.Null((await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id)).PasswordResetTokenHash);
        Assert.Equal(0, email.SendCount);
    }

    // ── Reset by token ──────────────────────────────────────────────────────────────────────────

    private static async Task<Guid> SeedAdminWithResetTokenAsync(
        DbContextOptions<AppDbContext> options, DateTime expiry, bool isActive = true)
    {
        using var seed = new AppDbContext(options);
        var admin = AdminUser.Create("Head Admin", "head", "head@eemo.gov.ph", "OldPass123", AdminRole.SuperAdmin, Guid.NewGuid(), isActive: isActive);
        admin.MarkEmailVerified();
        admin.SetPasswordResetToken(Hash(RawToken), expiry, DateTime.UtcNow);
        admin.SetRefreshToken("existing-refresh", DateTime.UtcNow.AddDays(7));
        seed.AdminUsers.Add(admin);
        await seed.SaveChangesAsync();
        return admin.Id;
    }

    [Fact]
    public async Task Reset_ValidToken_ChangesPassword_ConsumesToken_AndRevokesSessions()
    {
        var options = Options();
        var id = await SeedAdminWithResetTokenAsync(options, DateTime.UtcNow.AddMinutes(30));
        var email = new RecordingEmailSender();

        using (var ctx = new AppDbContext(options))
        {
            var result = await new ResetPasswordByTokenCommandHandler(ctx, email, new FixedClock(DateTime.UtcNow), new IdentityPasswordHasher())
                .Handle(new ResetPasswordByTokenCommand(RawToken, "BrandNew123"), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.True(admin.Accepts("BrandNew123"));
        Assert.False(admin.Accepts("OldPass123"));
        Assert.Null(admin.PasswordResetTokenHash);          // single use
        Assert.Null(admin.RefreshToken);                    // existing sessions revoked
        Assert.False(admin.MustChangePassword);             // the user chose this password
        Assert.True(admin.IsActive);
        Assert.Equal(1, email.SendCount);                   // "password changed" notification
    }

    [Fact]
    public async Task Reset_ExpiredToken_IsRejected_AndPasswordUnchanged()
    {
        var options = Options();
        var id = await SeedAdminWithResetTokenAsync(options, DateTime.UtcNow.AddMinutes(-1));

        using (var ctx = new AppDbContext(options))
        {
            var result = await new ResetPasswordByTokenCommandHandler(ctx, new RecordingEmailSender(), new FixedClock(DateTime.UtcNow), new IdentityPasswordHasher())
                .Handle(new ResetPasswordByTokenCommand(RawToken, "BrandNew123"), default);
            Assert.False(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.True(admin.Accepts("OldPass123"));
    }

    [Fact]
    public async Task Reset_UnknownToken_IsRejected_WithGenericError()
    {
        var options = Options();
        await SeedAdminWithResetTokenAsync(options, DateTime.UtcNow.AddMinutes(30));

        using var ctx = new AppDbContext(options);
        var result = await new ResetPasswordByTokenCommandHandler(ctx, new RecordingEmailSender(), new FixedClock(DateTime.UtcNow), new IdentityPasswordHasher())
            .Handle(new ResetPasswordByTokenCommand("some-other-token", "BrandNew123"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("This password reset link is invalid or has expired.", result.Error);
    }

    [Fact]
    public async Task Reset_TokenCannotBeReused()
    {
        var options = Options();
        await SeedAdminWithResetTokenAsync(options, DateTime.UtcNow.AddMinutes(30));

        using (var ctx = new AppDbContext(options))
        {
            var first = await new ResetPasswordByTokenCommandHandler(ctx, new RecordingEmailSender(), new FixedClock(DateTime.UtcNow), new IdentityPasswordHasher())
                .Handle(new ResetPasswordByTokenCommand(RawToken, "BrandNew123"), default);
            Assert.True(first.IsSuccess);
        }

        using (var ctx = new AppDbContext(options))
        {
            var second = await new ResetPasswordByTokenCommandHandler(ctx, new RecordingEmailSender(), new FixedClock(DateTime.UtcNow), new IdentityPasswordHasher())
                .Handle(new ResetPasswordByTokenCommand(RawToken, "Another456"), default);
            Assert.False(second.IsSuccess);
        }
    }

    [Fact]
    public async Task Reset_DeactivatedAccount_IsRejected_AndStaysDeactivated()
    {
        var options = Options();
        var id = await SeedAdminWithResetTokenAsync(options, DateTime.UtcNow.AddMinutes(30), isActive: false);

        using (var ctx = new AppDbContext(options))
        {
            var result = await new ResetPasswordByTokenCommandHandler(ctx, new RecordingEmailSender(), new FixedClock(DateTime.UtcNow), new IdentityPasswordHasher())
                .Handle(new ResetPasswordByTokenCommand(RawToken, "BrandNew123"), default);
            Assert.False(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var admin = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.False(admin.IsActive);                        // a reset link can never re-enable an account
        Assert.True(admin.Accepts("OldPass123"));     // password untouched
        Assert.Null(admin.PasswordResetTokenHash);           // stale token consumed so it cannot be retried
    }

    // ── Activation marks the email verified (what makes reset eligible) ──────────────────────────

    [Fact]
    public void CompleteActivation_MarksEmailVerified()
    {
        var admin = AdminUser.Create("Head", "head", "head@eemo.gov.ph", "placeholder", AdminRole.SuperAdmin, Guid.NewGuid(), isActive: false);
        Assert.False(admin.EmailVerified);

        admin.CompleteActivation("ChosenPass123");

        Assert.True(admin.EmailVerified);
        Assert.True(admin.IsActive);
    }
}
