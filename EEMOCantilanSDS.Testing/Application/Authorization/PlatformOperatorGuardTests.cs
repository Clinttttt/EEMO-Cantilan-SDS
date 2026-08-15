using EEMOCantilanSDS.Infrastructure.Security;
using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Authorization;

/// <summary>
/// The platform-operator guard, over a real context.
///
/// <para>
/// Activation had its own inlined copy of this decision that accepted only the default tenant's SuperAdmin. A DEDICATED
/// operator account — the mechanism meant to replace that fallback — could therefore approve an LGU's onboarding and
/// then be refused the activation that completes it. Nothing covered it, which is how the two drifted apart.
/// </para>
/// </summary>
public class PlatformOperatorGuardTests
{
    private sealed class Caller(Guid? userId, string? role, Guid? municipalityId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? Role => role;
        public Guid? MunicipalityId => municipalityId;
        public string? Username => "test";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public bool IsAuthenticated => userId is not null;

        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"operator-guard-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task ADedicatedOperatorIsAccepted_EvenBelongingToNoDefaultTenant()
    {
        var context = NewContext();

        var otherLgu = Municipality.Create(
            "SDS-TEST", "Somewhere", "Surigao del Sur", MunicipalityStatus.Active, "sds-test", isDefault: false);
        var cantilan = Municipality.Create(
            "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, "cantilan-sds", isDefault: true);
        context.Add(otherLgu);
        context.Add(cantilan);

        var console = AdminUser.Create(
            "Console", "console", "console@stalltrack.site", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin,
            isPlatformOperator: true);
        context.Add(console);
        await context.SaveChangesAsync();

        // Not the default tenant, and that is the point: an operator belongs to no LGU.
        var accepted = await PlatformOperatorGuard.IsCurrentAsync(
            context, new Caller(console.Id, "SuperAdmin", otherLgu.Id), CancellationToken.None);

        Assert.True(accepted);
    }

    [Fact]
    public async Task TheDefaultTenantsHeadIsStillAccepted()
    {
        var context = NewContext();

        var cantilan = Municipality.Create(
            "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, "cantilan-sds", isDefault: true);
        context.Add(cantilan);

        var head = AdminUser.Create("Head", "head", "head@eemo.gov", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin);
        context.Add(head);
        await context.SaveChangesAsync();

        var accepted = await PlatformOperatorGuard.IsCurrentAsync(
            context, new Caller(head.Id, "SuperAdmin", cantilan.Id), CancellationToken.None);

        Assert.True(accepted);
    }

    [Fact]
    public async Task AnotherLgusHeadIsRefused()
    {
        var context = NewContext();

        var otherLgu = Municipality.Create(
            "SDS-TEST", "Somewhere", "Surigao del Sur", MunicipalityStatus.Active, "sds-test", isDefault: false);
        var cantilan = Municipality.Create(
            "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, "cantilan-sds", isDefault: true);
        context.Add(otherLgu);
        context.Add(cantilan);

        var head = AdminUser.Create("Head", "head2", "head@sds.gov", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin);
        context.Add(head);
        await context.SaveChangesAsync();

        // Onboarding, activation, backup and restore reach across every LGU in the shared database. A municipal Head
        // must never hold them, however senior they are in their own municipality.
        var accepted = await PlatformOperatorGuard.IsCurrentAsync(
            context, new Caller(head.Id, "SuperAdmin", otherLgu.Id), CancellationToken.None);

        Assert.False(accepted);
    }

    [Fact]
    public async Task AnUnauthenticatedCallerIsRefused()
    {
        var context = NewContext();
        context.Add(Municipality.Create(
            "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, "cantilan-sds", isDefault: true));
        await context.SaveChangesAsync();

        var accepted = await PlatformOperatorGuard.IsCurrentAsync(
            context, new Caller(null, "SuperAdmin", null), CancellationToken.None);

        Assert.False(accepted);
    }

    [Fact]
    public async Task ADedicatedOperatorMayActivate_NotOnlyTheDefaultTenantsHead()
    {
        // Activation had its OWN copy of the operator rule, accepting only the default tenant's SuperAdmin. So a
        // dedicated operator account could approve an LGU's onboarding and then be refused the activation that
        // completes it - the mechanism meant to replace the fallback did not work end to end.
        var context = NewContext();

        var otherLgu = Municipality.Create(
            "SDS-TEST", "Somewhere", "Surigao del Sur", MunicipalityStatus.Active, "sds-test", isDefault: false);
        context.Add(otherLgu);
        context.Add(Municipality.Create(
            "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, "cantilan-sds", isDefault: true));

        var console = AdminUser.Create(
            "Console", "console", "console@stalltrack.site", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin,
            isPlatformOperator: true);
        context.Add(console);
        await context.SaveChangesAsync();

        var handler = new ActivateMunicipalityCommandHandler(
            context, new Caller(console.Id, "SuperAdmin", otherLgu.Id), new SilentEmail(), new IdentityPasswordHasher());

        var result = await handler.Handle(
            new ActivateMunicipalityCommand(
                MunicipalityCode: "NOT-A-REAL-CODE",
                Branding: new ActivationBranding("Economic Enterprise Office", null, null),
                Administrator: new ActivationAdministrator("Ana Cruz", "acruz", "acruz@lgu.gov.ph"),
                Facilities: new List<ActivationFacility>(),
                Rates: new List<ActivationRate>()),
            CancellationToken.None);

        // Past authorization is all this asserts: the code names no municipality on record, so the handler fails for
        // THAT reason. Forbidden would mean the operator was turned away at the door.
        Assert.NotEqual(ResultStatus.Forbidden, result.Status);
    }

    private sealed class SilentEmail : EEMOCantilanSDS.Application.Common.Interface.Services.IEmailSender
    {
        public Task<bool> SendAsync(string to, string? displayName, string subject, string htmlBody,
            CancellationToken ct = default) => Task.FromResult(true);
    }
}
