using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CollectionLedgerPersistenceTests(PostgresFixture db)
{
    private sealed record RevenueRef(Guid MunicipalityId, Guid ClassificationId, Guid PolicyId);

    private sealed class RestoreActor(Guid municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => null;
        public string? Username => "collection-restore-test";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "collection-restore-test";
        public Guid? MunicipalityId => municipalityId;
    }

    private async Task<Guid> CreateMunicipalityAsync(string code)
    {
        var municipality = Municipality.Create(code, code, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());
        await using var context = db.CreateContext(Guid.Empty);
        context.Municipalities.Add(municipality);
        await context.SaveChangesAsync();
        return municipality.Id;
    }

    private async Task<RevenueRef> CreateRevenueRefAsync(Guid tenantId, string code)
    {
        await using var context = db.CreateContext(tenantId);
        var classification = RevenueClassification.Create(code, tenantId);
        var policy = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 1), $"{code} label", RevenueInstrumentType.CashTicket, tenantId);
        context.AddRange(classification, policy);
        await context.SaveChangesAsync();
        return new RevenueRef(tenantId, classification.Id, policy.Id);
    }

    private async Task<Collection> CreateCollectionAsync(
        RevenueRef reference,
        decimal amount = 125m,
        Guid? clientOperationId = null,
        CollectionSourceKind? sourceKind = null,
        Guid? sourceId = null,
        CollectionSourcePart? sourcePart = null)
    {
        await using var context = db.CreateContext(reference.MunicipalityId);
        var classification = await context.RevenueClassifications.SingleAsync(x => x.Id == reference.ClassificationId);
        var policy = await context.RevenueClassificationPolicies.SingleAsync(x => x.Id == reference.PolicyId);
        var collection = Collection.Post(
            new DateOnly(2026, 9, 23),
            DateTime.SpecifyKind(new DateTime(2026, 9, 23, 8, 30, 0), DateTimeKind.Utc),
            "collector-17", "Field Collector", "Collector",
            [new CollectionLineDraft(classification, policy, amount, sourceKind, sourceId, sourcePart)],
            collectorId: Guid.NewGuid(),
            payerName: "Walk-in payer",
            clientOperationId: clientOperationId);
        context.Collections.Add(collection);
        await context.SaveChangesAsync();
        return collection;
    }

    [SkippableFact]
    public async Task CollectionsAndLinesAreTenantFilteredAndUnresolvedReadsFailClosed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("LEDGER-A");
        var tenantB = await CreateMunicipalityAsync("LEDGER-B");
        var refA = await CreateRevenueRefAsync(tenantA, "MARKET_FEES");
        var refB = await CreateRevenueRefAsync(tenantB, "MARKET_FEES");
        var collectionA = await CreateCollectionAsync(refA, 10m);
        var collectionB = await CreateCollectionAsync(refB, 20m);

        await using (var asA = db.CreateContext(tenantA))
        {
            Assert.Equal(collectionA.Id, (await asA.Collections.SingleAsync()).Id);
            Assert.Equal(collectionA.Lines.Single().Id, (await asA.CollectionLines.SingleAsync()).Id);
        }
        await using (var asB = db.CreateContext(tenantB))
        {
            Assert.Equal(collectionB.Id, (await asB.Collections.SingleAsync()).Id);
            Assert.Equal(collectionB.Lines.Single().Id, (await asB.CollectionLines.SingleAsync()).Id);
        }
        await using var unresolved = db.CreateContext(Guid.Empty);
        Assert.Empty(await unresolved.Collections.ToListAsync());
        Assert.Empty(await unresolved.CollectionLines.ToListAsync());
    }

    [SkippableFact]
    public async Task PostgreSqlRejectsCrossTenantParentsAndClassificationOrPolicyMismatch()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("LEDGER-FK-A");
        var tenantB = await CreateMunicipalityAsync("LEDGER-FK-B");
        var refA = await CreateRevenueRefAsync(tenantA, "MARKET_FEES");
        var refAOther = await CreateRevenueRefAsync(tenantA, "ECF");
        var refB = await CreateRevenueRefAsync(tenantB, "MARKET_FEES");
        var parentA = await CreateCollectionAsync(refA);
        var parentB = await CreateCollectionAsync(refB);

        await AssertInvalidLineAsync(tenantA, parentB.Id, refA.ClassificationId, refA.PolicyId); // parent tenant mismatch
        await AssertInvalidLineAsync(tenantA, parentA.Id, refB.ClassificationId, refB.PolicyId); // classification tenant mismatch
        await AssertInvalidLineAsync(tenantA, parentA.Id, refA.ClassificationId, refB.PolicyId); // policy tenant mismatch
        await AssertInvalidLineAsync(tenantA, parentA.Id, refA.ClassificationId, refAOther.PolicyId); // wrong classification policy
    }

    [SkippableFact]
    public async Task ClientOperationIdIsUniqueAcrossCollectionTableAndNullIsRepeatable()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("LEDGER-ID-A");
        var tenantB = await CreateMunicipalityAsync("LEDGER-ID-B");
        var refA = await CreateRevenueRefAsync(tenantA, "MARKET_FEES");
        var refB = await CreateRevenueRefAsync(tenantB, "MARKET_FEES");
        var operationId = Guid.NewGuid();
        await CreateCollectionAsync(refA, clientOperationId: operationId);

        await using (var duplicate = db.CreateContext(tenantB))
        {
            var classification = await duplicate.RevenueClassifications.SingleAsync();
            var policy = await duplicate.RevenueClassificationPolicies.SingleAsync();
            duplicate.Collections.Add(NewCollection(classification, policy, clientOperationId: operationId));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using var nullableOperations = db.CreateContext(tenantA);
        var classA = await nullableOperations.RevenueClassifications.SingleAsync();
        var policyA = await nullableOperations.RevenueClassificationPolicies.SingleAsync();
        nullableOperations.Collections.AddRange(NewCollection(classA, policyA), NewCollection(classA, policyA));
        await nullableOperations.SaveChangesAsync();
        Assert.Equal(3, await nullableOperations.Collections.CountAsync());
        Assert.Equal(2, await nullableOperations.Collections.CountAsync(x => x.ClientOperationId == null));
        Assert.Equal(1, await nullableOperations.Collections.CountAsync(x => x.ClientOperationId == operationId));
    }

    [SkippableFact]
    public async Task PostgreSqlRestrictsDeletingCollectionWithExistingLineHistory()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await CreateMunicipalityAsync("LEDGER-RESTRICT");
        var reference = await CreateRevenueRefAsync(tenant, "MARKET_FEES");
        var collection = await CreateCollectionAsync(reference);

        await using var context = db.CreateContext(tenant);
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Collections\" WHERE \"MunicipalityId\" = {tenant} AND \"Id\" = {collection.Id}"));
    }

    [SkippableFact]
    public async Task TenantRestoreRoundTripsCollectionLedgerAndLeavesAnotherTenantUntouched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("LEDGER-RESTORE-A");
        var tenantB = await CreateMunicipalityAsync("LEDGER-RESTORE-B");
        var refA = await CreateRevenueRefAsync(tenantA, "MARKET_FEES");
        var refB = await CreateRevenueRefAsync(tenantB, "MARKET_FEES");
        var operationA = Guid.NewGuid();
        var sourceIdA = Guid.NewGuid();
        var collectionA = await CreateCollectionAsync(
            refA, 230.75m, operationA, CollectionSourceKind.DailyCollection, sourceIdA, CollectionSourcePart.DailyFee);
        var collectionB = await CreateCollectionAsync(refB, 81.25m);

        var snapshot = await new TenantRestoreRepository(db.CreateContext(tenantA), new RestoreActor(tenantA))
            .CreateSnapshotAsync(CancellationToken.None);
        Assert.True(snapshot.Tables.ContainsKey("Collections"));
        Assert.True(snapshot.Tables.ContainsKey("CollectionLines"));

        await using (var changedA = db.CreateContext(tenantA))
        {
            await changedA.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Collections\" SET \"ActorName\" = {"Changed"} WHERE \"MunicipalityId\" = {tenantA} AND \"Id\" = {collectionA.Id}");
            await changedA.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"CollectionLines\" SET \"Amount\" = {999.99m} WHERE \"MunicipalityId\" = {tenantA} AND \"CollectionId\" = {collectionA.Id}");
        }

        var restore = await new TenantRestoreRepository(db.CreateContext(tenantA), new RestoreActor(tenantA))
            .RestoreAsync(snapshot, CancellationToken.None);
        Assert.Equal(1, restore.RowsPerTable["Collections"]);
        Assert.Equal(1, restore.RowsPerTable["CollectionLines"]);

        await using (var restoredA = db.CreateContext(tenantA))
        {
            var restored = await restoredA.Collections.Include(x => x.Lines).SingleAsync();
            Assert.Equal(collectionA.Id, restored.Id);
            Assert.Equal(new DateOnly(2026, 9, 23), restored.BusinessDate);
            Assert.Equal("Field Collector", restored.ActorName);
            Assert.Equal("collector-17", restored.ActorId);
            Assert.Equal("Collector", restored.ActorRole);
            Assert.Equal(230.75m, restored.TotalAmount);
            Assert.Equal(operationA, restored.ClientOperationId);
            var line = Assert.Single(restored.Lines);
            Assert.Equal(collectionA.Lines.Single().Id, line.Id);
            Assert.Equal(230.75m, line.Amount);
            Assert.Equal(refA.ClassificationId, line.RevenueClassificationId);
            Assert.Equal(refA.PolicyId, line.RevenueClassificationPolicyId);
            Assert.Equal(CollectionSourceKind.DailyCollection, line.SourceKind);
            Assert.Equal(sourceIdA, line.SourceId);
            Assert.Equal(CollectionSourcePart.DailyFee, line.SourcePart);
        }

        await using var unchangedB = db.CreateContext(tenantB);
        var remainsB = await unchangedB.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal(collectionB.Id, remainsB.Id);
        Assert.Equal(81.25m, remainsB.TotalAmount);
        Assert.Equal(81.25m, Assert.Single(remainsB.Lines).Amount);
    }

    private async Task AssertInvalidLineAsync(Guid tenantId, Guid collectionId, Guid classificationId, Guid policyId)
    {
        await using var context = db.CreateContext(Guid.Empty);
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "CollectionLines"
                ("Id", "MunicipalityId", "CollectionId", "RevenueClassificationId", "RevenueClassificationPolicyId", "Amount", "SourceKind", "SourceId", "SourcePart")
            VALUES ({Guid.NewGuid()}, {tenantId}, {collectionId}, {classificationId}, {policyId}, {10m}, {null}, {null}, {null})
            """));
    }

    private static Collection NewCollection(
        RevenueClassification classification,
        RevenueClassificationPolicy policy,
        Guid? clientOperationId = null) => Collection.Post(
            new DateOnly(2026, 9, 23),
            DateTime.SpecifyKind(new DateTime(2026, 9, 23, 8, 30, 0), DateTimeKind.Utc),
            "test-actor", "Test actor", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 10m)],
            clientOperationId: clientOperationId);
}
