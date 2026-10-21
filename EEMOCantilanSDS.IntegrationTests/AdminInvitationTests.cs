using System.Reflection;
using EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;
using EEMOCantilanSDS.Application.Command.Admins.ResendAdminInvitation;
using EEMOCantilanSDS.Application.Command.Admins.RevokeAdminInvitation;
using EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The Head's invitation of an Admin against a real PostgreSQL: the account exists INACTIVE with no password anyone knows, the
/// one-time link (stored only as a hash) activates it exactly once, an expired / reused / re-issued / withdrawn link does
/// nothing, and no invitation can reach across municipalities.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminInvitationTests(PostgresFixture db)
{
    public class NoOp : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? m, object?[]? args) =>
            m!.ReturnType == typeof(Task) ? Task.CompletedTask : m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null;
    }

    private sealed class Tenant(string code) : ITenantContext { public string TenantCode => code; }

    private sealed class Mail : IEmailSender
    {
        public readonly List<(string To, string Body)> Sent = [];
        public Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, CancellationToken ct = default)
        {
            Sent.Add((toEmail, body));
            return Task.FromResult(true);
        }
    }

    private sealed class Hasher : IPasswordHasher
    {
        public HashedPassword Hash(string password) => new("hash:" + password);
        public PasswordCheck Check(string hash, string password) => hash == "hash:" + password ? PasswordCheck.Succeeded : PasswordCheck.Failed;
    }

    private sealed class Clock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
        public DateTime PhilippineNow => DateTime.SpecifyKind(now.AddHours(8), DateTimeKind.Unspecified);
        public DateOnly PhilippineToday => DateOnly.FromDateTime(PhilippineNow);
    }

    private sealed class Caller(Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "x";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record Tenants(Guid A, Guid B);

    private async Task<Tenants> SeedAsync()
    {
        await db.ResetAsync();
        var a = Municipality.Create("TENANTA", "Tenant A", "Province", MunicipalityStatus.Active, tenantCode: "tenant-a");
        var b = Municipality.Create("TENANTB", "Tenant B", "Province", MunicipalityStatus.Active, tenantCode: "tenant-b");
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.AddRange(a, b);
        setup.AdminUsers.Add(AdminUser.Create("Head A", "head.a", "head.a@x.ph", new HashedPassword("h"), AdminRole.SuperAdmin, a.Id, mustChangePassword: false));
        setup.AdminUsers.Add(AdminUser.Create("Head B", "head.b", "head.b@x.ph", new HashedPassword("h"), AdminRole.SuperAdmin, b.Id, mustChangePassword: false));
        await setup.SaveChangesAsync();
        return new Tenants(a.Id, b.Id);
    }

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, NoOp>();

    private async Task<Result<EEMOCantilanSDS.Application.Dtos.AdminInvitationDto>> InviteAsync(
        Guid tenant, Mail mail, string username = "new.admin", string email = "new.admin@x.ph", string role = "SuperAdmin")
    {
        await using var ctx = db.CreateContext(tenant);
        var handler = new InviteAdminCommandHandler(new AdminRepository(ctx), new UnitOfWork(ctx), Proxy<IEemoCacheInvalidator>(),
            new Tenant("t"), new Hasher(), mail, new Clock(DateTime.UtcNow), new Caller(tenant, role));
        return await handler.Handle(new InviteAdminCommand("New Admin", username, email, AdminRole.Admin), CancellationToken.None);
    }

    private async Task<Result<bool>> AcceptAsync(string token, DateTime? now = null, string password = "Chosen-Pass1")
    {
        await using var ctx = db.CreateContext(Guid.Empty);
        return await new SetAdminPasswordByTokenCommandHandler(ctx, new Clock(now ?? DateTime.UtcNow), new Hasher())
            .Handle(new SetAdminPasswordByTokenCommand(token, password), CancellationToken.None);
    }

    private static string TokenOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    [SkippableFact]
    public async Task AnInvitedAdmin_ExistsInactive_WithNoPasswordAnyoneKnows_AndTheLinkIsStoredOnlyAsAHash()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        var mail = new Mail();

        var result = await InviteAsync(t.A, mail);

        Assert.True(result.IsSuccess, result.Error);
        var invite = result.Value!;
        Assert.False(invite.Admin.IsActive);
        Assert.True(invite.EmailSent);
        Assert.Single(mail.Sent);
        Assert.Contains(invite.ActivationUrl, mail.Sent[0].Body);
        Assert.StartsWith(ActivationLinks.Base, invite.ActivationUrl);

        await using var verify = db.CreateContext(Guid.Empty);
        var row = await verify.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.Username == "new.admin");
        Assert.Equal(t.A, row.MunicipalityId);
        Assert.False(row.IsActive);
        Assert.False(row.MustChangePassword);              // the password they set is their own choice
        Assert.True(row.HasPendingInvitation);
        Assert.DoesNotContain(TokenOf(invite.ActivationUrl), row.ActivationTokenHash!); // never the raw token
        Assert.Equal(ActivationTokens.Hash(TokenOf(invite.ActivationUrl)), row.ActivationTokenHash);
        // Nobody can sign in with a guessable or reused password: the placeholder is random and unshared.
        Assert.StartsWith("hash:Aa1!", row.PasswordHash);
        Assert.NotEqual(row.PasswordHash, "hash:Aa1!");
    }

    [SkippableFact]
    public async Task TheLink_ActivatesExactlyOnce_AndThenIsDead()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        var token = TokenOf((await InviteAsync(t.A, new Mail())).Value!.ActivationUrl);

        Assert.True((await AcceptAsync(token)).IsSuccess);

        await using (var verify = db.CreateContext(Guid.Empty))
        {
            var row = await verify.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.Username == "new.admin");
            Assert.True(row.IsActive);
            Assert.False(row.MustChangePassword);
            Assert.False(row.HasPendingInvitation);
            Assert.Equal("hash:Chosen-Pass1", row.PasswordHash);
            Assert.Equal(t.A, row.MunicipalityId);
            // The Head who invited them is untouched.
            var head = await verify.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.Username == "head.a");
            Assert.True(head.IsActive);
        }

        var again = await AcceptAsync(token, password: "Another-Pass2");
        Assert.False(again.IsSuccess);
        Assert.Equal("This activation link is invalid or has expired.", again.Error); // generic: says nothing about why
        await using var after = db.CreateContext(Guid.Empty);
        Assert.Equal("hash:Chosen-Pass1", (await after.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.Username == "new.admin")).PasswordHash);
    }

    [SkippableFact]
    public async Task AnExpiredLink_ActivatesNothing_AndTheHeadCanReissueIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        var mail = new Mail();
        var first = (await InviteAsync(t.A, mail)).Value!;

        var expired = await AcceptAsync(TokenOf(first.ActivationUrl), now: DateTime.UtcNow.AddDays(8));
        Assert.False(expired.IsSuccess);

        await using var ctx = db.CreateContext(t.A);
        var admin = await ctx.AdminUsers.SingleAsync(u => u.Username == "new.admin");
        Assert.False(admin.IsActive);
        Assert.True(admin.HasPendingInvitation); // still visible and re-issuable

        var resend = await new ResendAdminInvitationCommandHandler(new AdminRepository(ctx), new UnitOfWork(ctx), mail, new Clock(DateTime.UtcNow), new Caller(t.A, "SuperAdmin"))
            .Handle(new ResendAdminInvitationCommand(admin.Id), CancellationToken.None);
        Assert.True(resend.IsSuccess, resend.Error);
        // The earlier link is replaced, not kept alongside.
        Assert.False((await AcceptAsync(TokenOf(first.ActivationUrl))).IsSuccess);
        Assert.True((await AcceptAsync(TokenOf(resend.Value!.ActivationUrl))).IsSuccess);
    }

    [SkippableFact]
    public async Task ARevokedInvitation_CannotBeAccepted_AndItsUsernameIsFreeAgain()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        var first = (await InviteAsync(t.A, new Mail())).Value!;

        await using (var ctx = db.CreateContext(t.A))
        {
            var admin = await ctx.AdminUsers.SingleAsync(u => u.Username == "new.admin");
            var revoke = await new RevokeAdminInvitationCommandHandler(new AdminRepository(ctx), new UnitOfWork(ctx), Proxy<IEemoCacheInvalidator>(), new Tenant("t"), new Caller(t.A, "SuperAdmin"))
                .Handle(new RevokeAdminInvitationCommand(admin.Id), CancellationToken.None);
            Assert.True(revoke.IsSuccess, revoke.Error);
        }

        Assert.False((await AcceptAsync(TokenOf(first.ActivationUrl))).IsSuccess);
        await using var verify = db.CreateContext(Guid.Empty);
        Assert.Equal(0, await verify.AdminUsers.IgnoreQueryFilters().CountAsync(u => u.Username == "new.admin" && !u.IsDeleted));
    }

    [SkippableFact]
    public async Task AnAccountInUse_CannotBeRevokedOrReissued_ThroughTheInvitationControls()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        await using var ctx = db.CreateContext(t.A);
        var head = await ctx.AdminUsers.SingleAsync(u => u.Username == "head.a");

        var revoke = await new RevokeAdminInvitationCommandHandler(new AdminRepository(ctx), new UnitOfWork(ctx), Proxy<IEemoCacheInvalidator>(), new Tenant("t"), new Caller(t.A, "SuperAdmin"))
            .Handle(new RevokeAdminInvitationCommand(head.Id), CancellationToken.None);
        var resend = await new ResendAdminInvitationCommandHandler(new AdminRepository(ctx), new UnitOfWork(ctx), new Mail(), new Clock(DateTime.UtcNow), new Caller(t.A, "SuperAdmin"))
            .Handle(new ResendAdminInvitationCommand(head.Id), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, revoke.Status);
        Assert.Equal(ResultStatus.Conflict, resend.Status);
        Assert.True((await ctx.AdminUsers.SingleAsync(u => u.Username == "head.a")).IsActive);
    }

    [SkippableFact]
    public async Task OnlyTheHeadInvites_AndOneMunicipalityCannotTouchAnothersInvitation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();

        Assert.Equal(ResultStatus.Forbidden, (await InviteAsync(t.A, new Mail(), role: "Admin")).Status);
        Assert.Equal(ResultStatus.Forbidden, (await InviteAsync(t.A, new Mail(), role: "Collector")).Status);

        var invite = (await InviteAsync(t.A, new Mail())).Value!;

        // Tenant B's Head addresses A's pending account by id: it is not found for them, and nothing changes.
        await using var asB = db.CreateContext(t.B);
        var resend = await new ResendAdminInvitationCommandHandler(new AdminRepository(asB), new UnitOfWork(asB), new Mail(), new Clock(DateTime.UtcNow), new Caller(t.B, "SuperAdmin"))
            .Handle(new ResendAdminInvitationCommand(invite.Admin.Id), CancellationToken.None);
        var revoke = await new RevokeAdminInvitationCommandHandler(new AdminRepository(asB), new UnitOfWork(asB), Proxy<IEemoCacheInvalidator>(), new Tenant("t"), new Caller(t.B, "SuperAdmin"))
            .Handle(new RevokeAdminInvitationCommand(invite.Admin.Id), CancellationToken.None);
        Assert.Equal(ResultStatus.NotFound, resend.Status);
        Assert.Equal(ResultStatus.NotFound, revoke.Status);

        // A's link still works, and activates A's account under A - never B.
        Assert.True((await AcceptAsync(TokenOf(invite.ActivationUrl))).IsSuccess);
        await using var verify = db.CreateContext(Guid.Empty);
        var row = await verify.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.Username == "new.admin");
        Assert.Equal(t.A, row.MunicipalityId);
        Assert.Equal(0, await verify.AdminUsers.IgnoreQueryFilters().CountAsync(u => u.MunicipalityId == t.B && u.Username == "new.admin"));
    }

    [SkippableFact]
    public async Task AnInvalidLink_ChangesNoAccount()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var t = await SeedAsync();
        await InviteAsync(t.A, new Mail());

        foreach (var bad in new[] { "", "   ", "not-a-real-token", "../../etc/passwd" })
            Assert.False((await AcceptAsync(bad)).IsSuccess);

        await using var verify = db.CreateContext(Guid.Empty);
        Assert.False((await verify.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.Username == "new.admin")).IsActive);
    }
}
