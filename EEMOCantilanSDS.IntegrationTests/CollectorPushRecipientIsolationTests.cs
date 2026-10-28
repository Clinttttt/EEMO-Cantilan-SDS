using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CollectorPushRecipientIsolationTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task PaymentPushRecipients_AreActiveAssignedCollectorsInOnlyTheResolvedMunicipality()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var tenantA = Municipality.Create("PUSH-A", "Cantilan baseline", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: "push-a");
        var tenantB = Municipality.Create("PUSH-B", "Other municipality", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: "push-b");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.AddRange(tenantA, tenantB);
            await setup.SaveChangesAsync();
        }

        async Task<Guid> SeedAsync(Guid tenantId)
        {
            await using var context = db.CreateContext(tenantId);
            var tcc = Facility.Create(FacilityCode.TCC, "TCC", "TCC", municipalityId: tenantId);
            var npm = Facility.Create(FacilityCode.NPM, "NPM", "NPM", municipalityId: tenantId);
            CollectorUser Collector(string name) => CollectorUser.Create(name, name, name, null, null,
                new HashedPassword("test-hash"), tenantId);
            var assigned = Collector("assigned");
            var inactive = Collector("inactive");
            inactive.Deactivate("test");
            var elsewhere = Collector("elsewhere");
            var unassigned = Collector("unassigned");
            context.Facilities.AddRange(tcc, npm);
            context.CollectorUsers.AddRange(assigned, inactive, elsewhere, unassigned);
            await context.SaveChangesAsync();
            context.CollectorFacilityAssignments.AddRange(
                CollectorFacilityAssignment.Create(assigned.Id, tcc.Id, FacilityCode.TCC),
                CollectorFacilityAssignment.Create(inactive.Id, tcc.Id, FacilityCode.TCC),
                CollectorFacilityAssignment.Create(elsewhere.Id, npm.Id, FacilityCode.NPM));
            await context.SaveChangesAsync();
            return assigned.Id;
        }

        var assignedA = await SeedAsync(tenantA.Id);
        var assignedB = await SeedAsync(tenantB.Id);
        await using var asA = db.CreateContext(tenantA.Id);
        await using var asB = db.CreateContext(tenantB.Id);
        await using var unresolved = db.CreateContext(Guid.Empty);
        Assert.Equal(new[] { assignedA }, await new CollectorRepository(asA).GetActiveCollectorIdsByFacilityAsync(FacilityCode.TCC));
        Assert.Equal(new[] { assignedB }, await new CollectorRepository(asB).GetActiveCollectorIdsByFacilityAsync(FacilityCode.TCC));
        Assert.Empty(await new CollectorRepository(unresolved).GetActiveCollectorIdsByFacilityAsync(FacilityCode.TCC));
    }
}
