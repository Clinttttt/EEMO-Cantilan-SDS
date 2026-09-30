using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CollectorOperationAssignmentPersistenceTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task PostgresEnforcesTenantScopedUniquenessAndIsolation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        var tenantA = Municipality.Create($"OA-{Guid.NewGuid():N}"[..12], "Assignment A", "Province",
            MunicipalityStatus.Active, tenantCode: $"assignment-a-{Guid.NewGuid():N}"[..28]);
        var tenantB = Municipality.Create($"OB-{Guid.NewGuid():N}"[..12], "Assignment B", "Province",
            MunicipalityStatus.Active, tenantCode: $"assignment-b-{Guid.NewGuid():N}"[..28]);
        var collectorA = CollectorUser.Create("Collector A", "A-01", $"oa-{Guid.NewGuid():N}"[..14],
            null, null, new HashedPassword("test-hash"), tenantA.Id);
        var collectorB = CollectorUser.Create("Collector B", "B-01", $"ob-{Guid.NewGuid():N}"[..14],
            null, null, new HashedPassword("test-hash"), tenantB.Id);

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenantA, tenantB, collectorA, collectorB);
            await setup.SaveChangesAsync();
        }

        await using (var tenantContext = db.CreateContext(tenantA.Id))
        {
            tenantContext.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(
                tenantA.Id, collectorA.Id, CollectorOperationCodes.Wcf, "test-head"));
            await tenantContext.SaveChangesAsync();
        }

        await using (var tenantContext = db.CreateContext(tenantB.Id))
        {
            Assert.Empty(await tenantContext.CollectorOperationAssignments.ToListAsync());
            tenantContext.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(
                tenantB.Id, collectorB.Id, CollectorOperationCodes.Wcf, "test-head"));
            await tenantContext.SaveChangesAsync();
        }

        await using (var tenantContext = db.CreateContext(tenantA.Id))
        {
            tenantContext.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(
                tenantA.Id, collectorA.Id, CollectorOperationCodes.Wcf, "test-head"));
            await Assert.ThrowsAsync<DbUpdateException>(() => tenantContext.SaveChangesAsync());
        }

        // The composite FK rejects a tenant-A assignment that points at a tenant-B collector.
        await using (var tenantContext = db.CreateContext(tenantA.Id))
        {
            tenantContext.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(
                tenantA.Id, collectorB.Id, CollectorOperationCodes.MarketFees, "test-head"));
            await Assert.ThrowsAsync<DbUpdateException>(() => tenantContext.SaveChangesAsync());
        }
    }
}
