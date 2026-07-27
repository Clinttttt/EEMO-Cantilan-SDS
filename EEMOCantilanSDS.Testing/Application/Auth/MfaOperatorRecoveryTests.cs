using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetMfaEnrolledAccounts;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// Platform-operator two-factor recovery (Slice 3A).
/// <para>
/// This is the ONLY rescue path for a Head who lost both their authenticator device and their recovery codes:
/// peer Heads are blocked from each other's accounts, and self-service recovery restores a password, not a
/// second factor. These tests pin who may use it, what it requires, and that it clears the second factor
/// WITHOUT touching the target's password, role or active state.
/// </para>
/// </summary>
public class MfaOperatorRecoveryTests
{
    private const string OperatorPassword = "OpPass123!";
    private const string TargetPassword = "TargetPass123!";

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    /// <summary>Minimal ICurrentUserService stand-in (the guard reads id, role and municipality).</summary>
    private sealed class FakeCurrentUser(Guid? userId, Guid? municipalityId, string? role) : ICurrentUserService
    {
        public bool IsAuthenticated => userId is not null;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "acting";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    /// <summary>Cantilan (default LGU) + Carmen, an operator, a Carmen Head with MFA on, and a plain admin.</summary>
    private static async Task<(Guid operatorId, Guid carmenHeadId, Guid cantilanId, Guid carmenId)> SeedAsync(
        DbContextOptions<AppDbContext> options, bool operatorFlag = true)
    {
        using var seed = new AppDbContext(options);

        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true, officeAcronym: "EEMO");
        var carmen = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "carmen", officeAcronym: "CEEO");
        seed.Municipalities.AddRange(cantilan, carmen);

        var op = AdminUser.Create("Console Op", "console.op", "op@stalltrack.ph", OperatorPassword,
            AdminRole.SuperAdmin, cantilan.Id, isActive: true, isPlatformOperator: operatorFlag);

        // A Carmen Head who has lost their phone AND used every recovery code.
        var carmenHead = AdminUser.Create("Carmen Head", "carmen.head", "head@carmen.gov.ph", TargetPassword,
            AdminRole.SuperAdmin, carmen.Id);
        carmenHead.BeginMfaEnrollment("enc:SECRET");
        carmenHead.ConfirmMfaEnrollment(100, Array.Empty<string>());

        var plainAdmin = AdminUser.Create("Staff", "staff", "staff@carmen.gov.ph", "StaffPass1!", AdminRole.Admin, carmen.Id);

        seed.AdminUsers.AddRange(op, carmenHead, plainAdmin);
        await seed.SaveChangesAsync();

        return (op.Id, carmenHead.Id, cantilan.Id, carmen.Id);
    }

    private static ResetUserMfaCommandHandler Handler(AppDbContext ctx, Guid? actingId, Guid? municipalityId, string? role) =>
        new(ctx, new FakeCurrentUser(actingId, municipalityId, role), NullLogger<ResetUserMfaCommandHandler>.Instance);

    // ── Authorization ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_ByPlatformOperator_ClearsTheSecondFactor_AcrossTenants()
    {
        var options = Options();
        var (operatorId, carmenHeadId, cantilanId, _) = await SeedAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            var result = await Handler(ctx, operatorId, cantilanId, "SuperAdmin")
                .Handle(new ResetUserMfaCommand(carmenHeadId, OperatorPassword), default);
            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var head = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == carmenHeadId);

        Assert.False(head.MfaEnabled);
        Assert.Null(head.MfaSecretCipher);
        Assert.Null(head.MfaRecoveryCodeHashes);
        Assert.Null(head.MfaChallengeTokenHash);
        // Nothing else about the account may change — they sign in with their existing password.
        Assert.True(head.VerifyPassword(TargetPassword));
        Assert.True(head.IsActive);
        Assert.Equal(AdminRole.SuperAdmin, head.Role);
    }

    /// <summary>A per-LGU Head is NOT the platform operator and must never clear anyone else's second factor.</summary>
    [Fact]
    public async Task Reset_ByNonOperatorHead_IsForbidden()
    {
        var options = Options();
        var (_, carmenHeadId, _, carmenId) = await SeedAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            // Acting as a Carmen SuperAdmin (not the default LGU, no operator flag).
            var result = await Handler(ctx, carmenHeadId, carmenId, "SuperAdmin")
                .Handle(new ResetUserMfaCommand(carmenHeadId, TargetPassword), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(403, result.StatusCode);
        }

        using var verify = new AppDbContext(options);
        Assert.True((await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == carmenHeadId)).MfaEnabled);
    }

    [Fact]
    public async Task Reset_WithWrongOperatorPassword_IsRejected()
    {
        var options = Options();
        var (operatorId, carmenHeadId, cantilanId, _) = await SeedAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            var result = await Handler(ctx, operatorId, cantilanId, "SuperAdmin")
                .Handle(new ResetUserMfaCommand(carmenHeadId, "WrongPassword!"), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(400, result.StatusCode);
        }

        using var verify = new AppDbContext(options);
        Assert.True((await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == carmenHeadId)).MfaEnabled);
    }

    [Fact]
    public async Task Reset_UnknownAccount_IsNotFound()
    {
        var options = Options();
        var (operatorId, _, cantilanId, _) = await SeedAsync(options);

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, operatorId, cantilanId, "SuperAdmin")
            .Handle(new ResetUserMfaCommand(Guid.NewGuid(), OperatorPassword), default);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Reset_AccountWithoutMfa_IsRefused()
    {
        var options = Options();
        var (operatorId, _, cantilanId, _) = await SeedAsync(options);
        Guid plainId;
        using (var read = new AppDbContext(options))
            plainId = (await read.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Username == "staff")).Id;

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, operatorId, cantilanId, "SuperAdmin")
            .Handle(new ResetUserMfaCommand(plainId, OperatorPassword), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrolledAccounts_ListsAcrossLgus_ForTheOperatorOnly()
    {
        var options = Options();
        var (operatorId, carmenHeadId, cantilanId, carmenId) = await SeedAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            var allowed = await new GetMfaEnrolledAccountsQueryHandler(ctx, new FakeCurrentUser(operatorId, cantilanId, "SuperAdmin"))
                .Handle(new GetMfaEnrolledAccountsQuery(), default);

            Assert.True(allowed.IsSuccess);
            var account = Assert.Single(allowed.Value!);          // only the Carmen Head has MFA on
            Assert.Equal("carmen.head", account.Username);
            Assert.Equal("Carmen", account.Municipality);
            Assert.Equal("CEEO", account.OfficeAcronym);
            Assert.True(account.IsHead);
            Assert.Equal(0, account.RecoveryCodesRemaining);
        }
    }

    [Fact]
    public async Task EnrolledAccounts_NonOperatorHead_IsDenied()
    {
        // A Head of a non-default municipality is not an operator at all.
        var options = Options();
        var (_, carmenHeadId, _, carmenId) = await SeedAsync(options);

        using var ctx = new AppDbContext(options);
        var denied = await new GetMfaEnrolledAccountsQueryHandler(ctx, new FakeCurrentUser(carmenHeadId, carmenId, "SuperAdmin"))
            .Handle(new GetMfaEnrolledAccountsQuery(), default);

        Assert.False(denied.IsSuccess);
        Assert.Equal(403, denied.StatusCode);
    }

    [Fact]
    public async Task EnrolledAccounts_FallbackOperator_SeesOnlyItsOwnMunicipality()
    {
        // The Cantilan Head is a municipal officer who merely INHERITS operator powers (no
        // IsPlatformOperator flag). Regression: the recovery list showed every LGU's Head — Carrascal's and
        // Madrid's usernames and work emails appeared inside Cantilan's own portal.
        var options = Options();
        var (fallbackHeadId, _, cantilanId, _) = await SeedAsync(options, operatorFlag: false);

        // Give the Cantilan side an enrolled account of its own so "empty" cannot be mistaken for "scoped".
        using (var seed = new AppDbContext(options))
        {
            var cantilanAdmin = AdminUser.Create("Cantilan Clerk", "cantilan.clerk", "clerk@cantilan.gov.ph",
                "ClerkPass1!", AdminRole.Admin, cantilanId);
            cantilanAdmin.BeginMfaEnrollment("enc:SECRET");
            cantilanAdmin.ConfirmMfaEnrollment(100, Array.Empty<string>());
            seed.AdminUsers.Add(cantilanAdmin);
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options);
        var result = await new GetMfaEnrolledAccountsQueryHandler(ctx, new FakeCurrentUser(fallbackHeadId, cantilanId, "SuperAdmin"))
            .Handle(new GetMfaEnrolledAccountsQuery(), default);

        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value!);
        Assert.Equal("cantilan.clerk", account.Username);          // its own municipality only
        Assert.DoesNotContain(result.Value!, a => a.Username == "carmen.head");
    }

    [Fact]
    public async Task Reset_FallbackOperator_CannotClearAnotherMunicipalitysHead()
    {
        // Scoping the list is not enough on its own: the reset takes an account id, so the same confinement
        // has to hold on the write path.
        var options = Options();
        var (fallbackHeadId, carmenHeadId, cantilanId, _) = await SeedAsync(options, operatorFlag: false);

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, fallbackHeadId, cantilanId, "SuperAdmin")
            .Handle(new ResetUserMfaCommand(carmenHeadId, OperatorPassword), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);   // never confirms an out-of-scope account exists

        // And the target's second factor is untouched.
        using var verify = new AppDbContext(options);
        var carmenHead = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == carmenHeadId);
        Assert.True(carmenHead.MfaEnabled);
    }
}
