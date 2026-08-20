using EEMOCantilanSDS.Infrastructure.Security;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Authorization;
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
/// Two-factor recovery: who may clear a second factor, and whose.
/// <para>
/// An office administers its own staff. A Head clears the second factor for accounts in their OWN
/// municipality, under the ordinary peer-Head rule — their own account and Admin accounts, never another
/// Head's. Requiring platform-operator for this left every LGU but the default one unable to help its own
/// clerk who lost a phone.
/// </para>
/// <para>
/// A locked-out HEAD is the one case an office cannot solve alone, and self-service recovery restores a
/// password, not a second factor — so reaching across municipalities remains the dedicated platform
/// operator's rescue. These tests pin both halves, what the action requires, and that it clears the second
/// factor WITHOUT touching the target's password, role or active state.
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

        var op = AdminUser.Create("Console Op", "console.op", "op@stalltrack.ph", TestPasswords.Hash(OperatorPassword),
            AdminRole.SuperAdmin, cantilan.Id, isActive: true, isPlatformOperator: operatorFlag);

        // A Carmen Head who has lost their phone AND used every recovery code.
        var carmenHead = AdminUser.Create("Carmen Head", "carmen.head", "head@carmen.gov.ph", TestPasswords.Hash(TargetPassword),
            AdminRole.SuperAdmin, carmen.Id);
        carmenHead.BeginMfaEnrollment("enc:SECRET");
        carmenHead.ConfirmMfaEnrollment(100, Array.Empty<string>());

        var plainAdmin = AdminUser.Create("Staff", "staff", "staff@carmen.gov.ph", TestPasswords.Hash("StaffPass1!"), AdminRole.Admin, carmen.Id);

        seed.AdminUsers.AddRange(op, carmenHead, plainAdmin);
        await seed.SaveChangesAsync();

        return (op.Id, carmenHead.Id, cantilan.Id, carmen.Id);
    }

    private static ResetUserMfaCommandHandler Handler(AppDbContext ctx, Guid? actingId, Guid? municipalityId, string? role) =>
        new(ctx, new FakeCurrentUser(actingId, municipalityId, role), NullLogger<ResetUserMfaCommandHandler>.Instance, new IdentityPasswordHasher());

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
        Assert.True(head.Accepts(TargetPassword));
        Assert.True(head.IsActive);
        Assert.Equal(AdminRole.SuperAdmin, head.Role);
    }

    /// <summary>
    /// An office administers its own staff: its Head clears the second factor on its own Admin account. This
    /// is what used to be refused — every LGU except the default one had to ask the platform to unlock a clerk.
    /// </summary>
    [Fact]
    public async Task Reset_ByItsOwnHead_ClearsAStaffAccountOfThatOffice()
    {
        var options = Options();
        var (_, carmenHeadId, _, carmenId) = await SeedAsync(options);

        // A Carmen clerk with two-factor on, who has lost their phone and their codes.
        Guid clerkId;
        using (var seed = new AppDbContext(options))
        {
            var clerk = AdminUser.Create("Carmen Clerk", "carmen.clerk", "clerk@carmen.gov.ph",
                TestPasswords.Hash("ClerkPass1!"), AdminRole.Admin, carmenId);
            clerk.BeginMfaEnrollment("enc:SECRET");
            clerk.ConfirmMfaEnrollment(100, Array.Empty<string>());
            seed.AdminUsers.Add(clerk);
            await seed.SaveChangesAsync();
            clerkId = clerk.Id;
        }

        using (var ctx = new AppDbContext(options))
        {
            // Carmen's own Head, no operator flag, not the default municipality.
            var result = await Handler(ctx, carmenHeadId, carmenId, "SuperAdmin")
                .Handle(new ResetUserMfaCommand(clerkId, TargetPassword), default);

            Assert.True(result.IsSuccess);
        }

        using var verify = new AppDbContext(options);
        var clerkAfter = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == clerkId);
        Assert.False(clerkAfter.MfaEnabled);
        Assert.Null(clerkAfter.MfaSecretCipher);
        // Nothing else about the account changes — they sign in with their existing password and enrol again.
        Assert.True(clerkAfter.Accepts("ClerkPass1!"));
        Assert.True(clerkAfter.IsActive);
        Assert.Equal(AdminRole.Admin, clerkAfter.Role);
    }

    /// <summary>
    /// A Head may not clear a PEER Head's second factor, in their own office or anywhere else — the same
    /// peer-Head rule that governs every other admin-management action. That case is the operator's rescue.
    /// </summary>
    [Fact]
    public async Task Reset_ByAHead_OnAPeerHeadOfTheSameOffice_IsRefused()
    {
        var options = Options();
        var (_, carmenHeadId, _, carmenId) = await SeedAsync(options);

        Guid secondHeadId;
        using (var seed = new AppDbContext(options))
        {
            var peer = AdminUser.Create("Carmen Second Head", "carmen.head2", "head2@carmen.gov.ph",
                TestPasswords.Hash("Head2Pass1!"), AdminRole.SuperAdmin, carmenId);
            seed.AdminUsers.Add(peer);
            await seed.SaveChangesAsync();
            secondHeadId = peer.Id;
        }

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, carmenHeadId, carmenId, "SuperAdmin")
            .Handle(new ResetUserMfaCommand(secondHeadId, TargetPassword), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(AdminManagementGuard.PeerHeadDenied, result.Error);
    }

    /// <summary>An ordinary Admin administers nobody's second factor, not even in their own office.</summary>
    [Fact]
    public async Task Reset_ByAnOrdinaryAdmin_IsForbidden()
    {
        var options = Options();
        var (_, carmenHeadId, _, carmenId) = await SeedAsync(options);

        Guid staffId;
        using (var ctx = new AppDbContext(options))
        {
            staffId = (await ctx.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Username == "staff")).Id;
        }

        using (var ctx = new AppDbContext(options))
        {
            var result = await Handler(ctx, staffId, carmenId, "Admin")
                .Handle(new ResetUserMfaCommand(carmenHeadId, "StaffPass1!"), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(ResultStatus.Forbidden, result.Status);
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
            Assert.Equal(ResultStatus.Invalid, result.Status);
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

        Assert.Equal(ResultStatus.NotFound, result.Status);
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
        Assert.Equal(ResultStatus.Invalid, result.Status);
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
    public async Task EnrolledAccounts_ByAHead_ShowsOnlyItsOwnOffice()
    {
        // A Head administers their own staff, so they see their own municipality's enrolled accounts — and
        // only those. Another LGU's usernames and work e-mails have no business inside this portal.
        var options = Options();
        var (_, carmenHeadId, cantilanId, carmenId) = await SeedAsync(options);

        using (var seed = new AppDbContext(options))
        {
            var cantilanAdmin = AdminUser.Create("Cantilan Clerk", "cantilan.clerk", "clerk@cantilan.gov.ph",
                TestPasswords.Hash("ClerkPass1!"), AdminRole.Admin, cantilanId);
            cantilanAdmin.BeginMfaEnrollment("enc:SECRET");
            cantilanAdmin.ConfirmMfaEnrollment(100, Array.Empty<string>());
            seed.AdminUsers.Add(cantilanAdmin);
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options);
        var result = await new GetMfaEnrolledAccountsQueryHandler(ctx, new FakeCurrentUser(carmenHeadId, carmenId, "SuperAdmin"))
            .Handle(new GetMfaEnrolledAccountsQuery(), default);

        Assert.True(result.IsSuccess);
        var account = Assert.Single(result.Value!);
        Assert.Equal("carmen.head", account.Username);
        Assert.DoesNotContain(result.Value!, a => a.Username == "cantilan.clerk");
    }

    [Fact]
    public async Task EnrolledAccounts_ByAnOrdinaryAdmin_IsDenied()
    {
        // The recovery list names accounts and their two-factor state; it is for whoever administers them.
        var options = Options();
        var (_, _, _, carmenId) = await SeedAsync(options);

        using var ctx = new AppDbContext(options);
        var staffId = (await ctx.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Username == "staff")).Id;

        var denied = await new GetMfaEnrolledAccountsQueryHandler(ctx, new FakeCurrentUser(staffId, carmenId, "Admin"))
            .Handle(new GetMfaEnrolledAccountsQuery(), default);

        Assert.False(denied.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, denied.Status);
    }

    [Fact]
    public async Task EnrolledAccounts_TheDefaultOfficesHead_SeesOnlyItsOwnOffice()
    {
        // The default municipality's Head is a municipal officer like any other, whatever powers that office
        // has inherited. Regression: the recovery list showed every LGU's Head — Carrascal's and Madrid's
        // usernames and work emails appeared inside Cantilan's own portal.
        var options = Options();
        var (fallbackHeadId, _, cantilanId, _) = await SeedAsync(options, operatorFlag: false);

        // Give the Cantilan side an enrolled account of its own so "empty" cannot be mistaken for "scoped".
        using (var seed = new AppDbContext(options))
        {
            var cantilanAdmin = AdminUser.Create("Cantilan Clerk", "cantilan.clerk", "clerk@cantilan.gov.ph",
                TestPasswords.Hash("ClerkPass1!"), AdminRole.Admin, cantilanId);
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
    public async Task Reset_ByAHead_CannotReachAnotherMunicipality()
    {
        // Scoping the list is not enough on its own: the reset takes an account id, so the same confinement
        // has to hold on the write path. Only a DEDICATED operator crosses municipalities.
        var options = Options();
        var (fallbackHeadId, carmenHeadId, cantilanId, _) = await SeedAsync(options, operatorFlag: false);

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, fallbackHeadId, cantilanId, "SuperAdmin")
            .Handle(new ResetUserMfaCommand(carmenHeadId, OperatorPassword), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.NotFound, result.Status);   // never confirms an out-of-scope account exists

        // And the target's second factor is untouched.
        using var verify = new AppDbContext(options);
        var carmenHead = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == carmenHeadId);
        Assert.True(carmenHead.MfaEnabled);
    }
}
