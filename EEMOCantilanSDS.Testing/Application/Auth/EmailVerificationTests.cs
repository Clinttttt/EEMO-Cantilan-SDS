using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.VerifyEmail;
using EEMOCantilanSDS.Application.Queries.Auth.GetPasswordResetContext;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// Email confirmation is what makes self-service password recovery reachable: an account created in-app
/// starts UNVERIFIED and therefore cannot receive reset links, which is why confirming the address matters.
/// These tests pin the confirmation flow, the "changing the address revokes verification" rule, and the
/// reset-token → account lookup that tells a shared mailbox which link is which.
/// </summary>
public class EmailVerificationTests
{
    private const string RawToken = "verify-token-abc";

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    // ── Confirming the address ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyEmail_ValidToken_MarksVerified_AndConsumesToken()
    {
        var options = Options();
        Guid id, municipalityId = Guid.NewGuid();

        using (var seed = new AppDbContext(options))
        {
            var municipality = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            typeof(Municipality).GetProperty(nameof(Municipality.Id))!.SetValue(municipality, municipalityId);
            var admin = AdminUser.Create("Head Two", "head2", "head2@eemo.gov.ph", "OldPass123", AdminRole.SuperAdmin, municipalityId);
            admin.SetEmailVerificationToken(Hash(RawToken), DateTime.UtcNow.AddDays(7));
            seed.Municipalities.Add(municipality);
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
            id = admin.Id;
        }

        using (var ctx = new AppDbContext(options))
        {
            var result = await new VerifyEmailCommandHandler(ctx).Handle(new VerifyEmailCommand(RawToken), default);
            Assert.True(result.IsSuccess);
            Assert.Equal("head2", result.Value!.Username);
            Assert.False(result.Value.AlreadyVerified);
            Assert.Equal("Cantilan", result.Value.Municipality);
        }

        using var verify = new AppDbContext(options);
        var saved = await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id);
        Assert.True(saved.EmailVerified);
        Assert.Null(saved.EmailVerificationTokenHash);   // single use
        // Confirming grants nothing else.
        Assert.True(saved.VerifyPassword("OldPass123"));
    }

    [Fact]
    public async Task VerifyEmail_TokenCannotBeReused()
    {
        var options = Options();
        using (var seed = new AppDbContext(options))
        {
            var admin = AdminUser.Create("Head", "head", "head@eemo.gov.ph", "OldPass123", AdminRole.Admin, Guid.NewGuid());
            admin.SetEmailVerificationToken(Hash(RawToken), DateTime.UtcNow.AddDays(7));
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
        }

        using (var ctx = new AppDbContext(options))
            Assert.True((await new VerifyEmailCommandHandler(ctx).Handle(new VerifyEmailCommand(RawToken), default)).IsSuccess);

        using (var ctx = new AppDbContext(options))
            Assert.False((await new VerifyEmailCommandHandler(ctx).Handle(new VerifyEmailCommand(RawToken), default)).IsSuccess);
    }

    [Fact]
    public async Task VerifyEmail_ExpiredToken_IsRejected()
    {
        var options = Options();
        Guid id;
        using (var seed = new AppDbContext(options))
        {
            var admin = AdminUser.Create("Head", "head", "head@eemo.gov.ph", "OldPass123", AdminRole.Admin, Guid.NewGuid());
            admin.SetEmailVerificationToken(Hash(RawToken), DateTime.UtcNow.AddMinutes(-1));
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
            id = admin.Id;
        }

        using (var ctx = new AppDbContext(options))
            Assert.False((await new VerifyEmailCommandHandler(ctx).Handle(new VerifyEmailCommand(RawToken), default)).IsSuccess);

        using var verify = new AppDbContext(options);
        Assert.False((await verify.AdminUsers.IgnoreQueryFilters().FirstAsync(u => u.Id == id)).EmailVerified);
    }

    // ── Changing the address revokes verification ───────────────────────────────────────────────

    /// <summary>
    /// Regression: a replaced address must NOT inherit the previous address's trust — otherwise editing an
    /// admin's email would let an unproven mailbox receive password-reset links.
    /// </summary>
    [Fact]
    public void UpdateProfile_ChangingEmail_ClearsVerifiedAndPendingToken()
    {
        var admin = AdminUser.Create("Head", "head", "old@eemo.gov.ph", "OldPass123", AdminRole.Admin, Guid.NewGuid());
        admin.MarkEmailVerified();
        admin.SetEmailVerificationToken(Hash("pending"), DateTime.UtcNow.AddDays(7));
        Assert.True(admin.EmailVerified);

        admin.UpdateProfile("Head", "head", "new@eemo.gov.ph", "tester");

        Assert.False(admin.EmailVerified);
        Assert.Null(admin.EmailVerificationTokenHash);
    }

    [Fact]
    public void UpdateProfile_KeepingSameEmail_KeepsVerified()
    {
        var admin = AdminUser.Create("Head", "head", "same@eemo.gov.ph", "OldPass123", AdminRole.Admin, Guid.NewGuid());
        admin.MarkEmailVerified();

        // Same address (different casing) — a rename must not silently revoke verification.
        admin.UpdateProfile("New Name", "head", "SAME@eemo.gov.ph", "tester");

        Assert.True(admin.EmailVerified);
    }

    // ── Reset-token → account context ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetContext_ValidToken_NamesTheAccountAndLgu()
    {
        var options = Options();
        var municipalityId = Guid.NewGuid();

        using (var seed = new AppDbContext(options))
        {
            var municipality = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "carmen");
            typeof(Municipality).GetProperty(nameof(Municipality.Id))!.SetValue(municipality, municipalityId);
            var admin = AdminUser.Create("Carmen Head", "carmen.head", "shared@lgu.gov.ph", "OldPass123", AdminRole.SuperAdmin, municipalityId);
            admin.MarkEmailVerified();
            admin.SetPasswordResetToken(Hash(RawToken), DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow);
            seed.Municipalities.Add(municipality);
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options);
        var result = await new GetPasswordResetContextQueryHandler(ctx)
            .Handle(new GetPasswordResetContextQuery(RawToken), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("carmen.head", result.Value!.Username);
        Assert.Equal("Carmen", result.Value.Municipality);
    }

    [Fact]
    public async Task ResetContext_UnknownOrExpiredToken_IsRejected()
    {
        var options = Options();
        using (var seed = new AppDbContext(options))
        {
            var admin = AdminUser.Create("Head", "head", "head@eemo.gov.ph", "OldPass123", AdminRole.Admin, Guid.NewGuid());
            admin.SetPasswordResetToken(Hash(RawToken), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
            seed.AdminUsers.Add(admin);
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options);
        var handler = new GetPasswordResetContextQueryHandler(ctx);

        Assert.False((await handler.Handle(new GetPasswordResetContextQuery(RawToken), default)).IsSuccess);      // expired
        Assert.False((await handler.Handle(new GetPasswordResetContextQuery("nope"), default)).IsSuccess);        // unknown
    }
}
