using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AuditActorIdentityTests(PostgresFixture db)
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task ActorDirectoryAndSearchUseRecordedIdentityWithinTheTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var own = Municipality.Create("AUDIT-OWN", "Own office", "Test", MunicipalityStatus.Active, tenantCode: "audit-own");
        var other = Municipality.Create("AUDIT-OTHER", "Other office", "Test", MunicipalityStatus.Active, tenantCode: "audit-other");
        var head = AdminUser.Create("Own Head", "head", "own@test.invalid", new HashedPassword("h"), AdminRole.SuperAdmin);
        var foreign = AdminUser.Create("Other Head", "head", "other@test.invalid", new HashedPassword("h"), AdminRole.SuperAdmin);
        await using (var seed = db.CreateContext(Guid.Empty))
        {
            seed.AddRange(own, other, head, foreign);
            seed.Entry(head).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = own.Id;
            seed.Entry(foreign).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = other.Id;
            await seed.SaveChangesAsync();
        }
        await using var context = db.CreateContext(own.Id);
        var confirmed = AuditLog.Create(head.Id.ToString("N"), "previous.username", "SuperAdmin", "Updated", "AdminAccount");
        context.AuditLogs.AddRange(confirmed,
            AuditLog.Create(foreign.Id.ToString().ToUpperInvariant(), "head", "SuperAdmin", "Updated", "AdminAccount"),
            AuditLog.Create(foreign.Id.ToString("N").ToUpperInvariant(), "head", "SuperAdmin", "Updated", "AdminAccount"),
            AuditLog.Create(Guid.NewGuid().ToString(), "head", "SuperAdmin", "Updated", "AdminAccount"),
            AuditLog.Create("system", "system", "system", "Updated", "PaymentRecord"));
        await context.SaveChangesAsync();
        var tenant = new Tenant(own.Id);
        var repository = new AuditRepository(context, tenant);
        var trail = await repository.GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, true, default);
        Assert.Equal(3, trail.TotalEvents);
        Assert.Equal("Own Head", trail.Items.Single(x => x.Id == confirmed.Id).ActorDisplayName);
        Assert.Contains(trail.Items, x => x.ActorName == "head" && x.ActorDisplayName == "head");
        Assert.Contains(trail.Items, x => x.ActorDisplayName == "System");
        Assert.DoesNotContain(trail.Actors, x => x.Label == "Other Head");
        var search = await repository.GetAuditTrailAsync("Own Head", null, null, null, null, null, 1, 25, false, default);
        Assert.Contains(search.Items, x => x.Id == confirmed.Id);
    }
}
