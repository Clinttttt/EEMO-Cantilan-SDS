using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetTrmCollectionShadowReconciliation;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TrmCollectionShadowReconciliationTests(PostgresFixture db)
{
    private sealed record SeededTrip(Guid TripId, Guid? CollectorId, decimal Fee, string OrNumber);

    private sealed class Caller(Guid municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => null;
        public string? Username => "trm-shadow-integration-test";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "trm-shadow-integration-test";
        public Guid? MunicipalityId => municipalityId;
    }

    private async Task<Guid> CreateMunicipalityAsync(string code)
    {
        var municipality = Municipality.Create(
            code,
            code,
            "Surigao del Sur",
            MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());

        await using var context = db.CreateContext(Guid.Empty);
        context.Municipalities.Add(municipality);
        await context.SaveChangesAsync();
        return municipality.Id;
    }

    private async Task<SeededTrip> SeedTripAsync(
        Guid tenantId,
        DateTime recordedAtUtc,
        decimal fee,
        Guid? collectorId = null)
    {
        var orNumber = $"LEGACY-{Guid.NewGuid():N}"[..16];
        await using var context = db.CreateContext(tenantId);
        var trip = TrmTrip.Create(
            transporterId: null,
            tripNumber: 1,
            driverName: "Historical driver",
            plateNumber: "TRM 100",
            route: "Legacy route",
            orNumber: orNumber,
            organization: "Unassociated",
            collectorId: collectorId,
            createdBy: "integration-test",
            recordedAt: DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc),
            fee: fee);
        context.TrmTrips.Add(trip);
        await context.SaveChangesAsync();
        return new(trip.Id, collectorId, fee, orNumber);
    }

    [SkippableFact]
    public async Task PostgreSqlShadowQueryUsesStoredFeesAndPolicyAsOfBusinessDateWithoutWritingLedger()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = await CreateMunicipalityAsync($"TRM-A-{suffix}");
        var tenantB = await CreateMunicipalityAsync($"TRM-B-{suffix}");
        var collectorId = Guid.NewGuid();

        Guid classificationId;
        Guid effectivePolicyId;
        await using (var context = db.CreateContext(tenantA))
        {
            var classification = RevenueClassification.Create(
                RevenueClassificationCodes.TransportationParking,
                tenantA,
                "integration-test");
            classification.Retire("integration-test-retirement");
            var olderPolicy = RevenueClassificationPolicy.Create(
                classification.Id,
                new DateOnly(2026, 9, 1),
                "Older policy",
                RevenueInstrumentType.CashTicket,
                tenantA,
                createdBy: "integration-test");
            var effectivePolicy = RevenueClassificationPolicy.Create(
                classification.Id,
                new DateOnly(2026, 9, 20),
                "Effective policy",
                RevenueInstrumentType.CashTicket,
                tenantA,
                createdBy: "integration-test");
            var futurePolicy = RevenueClassificationPolicy.Create(
                classification.Id,
                new DateOnly(2026, 10, 1),
                "Future policy",
                RevenueInstrumentType.CashTicket,
                tenantA,
                createdBy: "integration-test");
            classificationId = classification.Id;
            effectivePolicyId = effectivePolicy.Id;
            context.AddRange(classification, olderPolicy, effectivePolicy, futurePolicy);
            await context.SaveChangesAsync();
        }

        // 30 Aug has no policy yet; the two September trips use persisted historical fees and the 20 Sep policy.
        var unresolvedTrip = await SeedTripAsync(
            tenantA, new DateTime(2026, 8, 29, 16, 0, 0), 80.00m);
        var firstProjectedTrip = await SeedTripAsync(
            tenantA, new DateTime(2026, 9, 22, 16, 0, 0), 37.81m, collectorId);
        var secondProjectedTrip = await SeedTripAsync(
            tenantA, new DateTime(2026, 9, 27, 16, 0, 0), 102.10m);
        await SeedTripAsync(tenantB, new DateTime(2026, 9, 22, 16, 0, 0), 9999.99m);

        await using var queryContext = db.CreateContext(tenantA);
        var collectionsBefore = await queryContext.Collections.CountAsync();
        var linesBefore = await queryContext.CollectionLines.CountAsync();
        var result = await new GetTrmCollectionShadowReconciliationQueryHandler(
                queryContext, new Caller(tenantA))
            .Handle(
                new(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 3)),
                default);
        var collectionsAfter = await queryContext.Collections.CountAsync();
        var linesAfter = await queryContext.CollectionLines.CountAsync();

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(3, reconciliation.SourceCount);
        Assert.Equal(2, reconciliation.ProjectedCount);
        Assert.Equal(1, reconciliation.UnresolvedCount);
        Assert.Equal(reconciliation.SourceCount, reconciliation.ProjectedCount + reconciliation.UnresolvedCount);
        Assert.Equal(219.91m, reconciliation.SourceTotal);
        Assert.Equal(139.91m, reconciliation.ProjectedTotal);
        Assert.Equal(80.00m, reconciliation.UnresolvedTotal);
        Assert.Equal(80.00m, reconciliation.Difference);
        Assert.Equal(reconciliation.SourceTotal, reconciliation.ProjectedTotal + reconciliation.UnresolvedTotal);
        Assert.False(reconciliation.IsReconciled);

        var projectedRows = reconciliation.ProjectedRows;
        Assert.Equal(new[] { firstProjectedTrip.TripId, secondProjectedTrip.TripId }, projectedRows.Select(x => x.TrmTripId));
        var firstRow = projectedRows[0];
        Assert.Equal(new DateOnly(2026, 9, 23), firstRow.BusinessDate);
        Assert.Equal(firstProjectedTrip.Fee, firstRow.Amount);
        Assert.Equal(firstProjectedTrip.CollectorId, firstRow.CollectorId);
        Assert.Equal(classificationId, firstRow.RevenueClassificationId);
        Assert.Equal(effectivePolicyId, firstRow.RevenueClassificationPolicyId);
        Assert.Equal(CollectionSourceKind.TrmTrip, firstRow.SourceKind);
        Assert.Equal(firstProjectedTrip.TripId, firstRow.SourceId);
        Assert.DoesNotContain(projectedRows, x => x.Amount == 9999.99m);

        var unresolved = Assert.Single(reconciliation.UnresolvedRows);
        Assert.Equal(unresolvedTrip.TripId, unresolved.TrmTripId);
        Assert.Equal(new DateOnly(2026, 8, 30), unresolved.BusinessDate);
        Assert.Equal(unresolvedTrip.Fee, unresolved.Amount);
        Assert.Equal(TrmCollectionShadowUnresolvedReasons.TransportationParkingPolicyNotEffectiveCode, unresolved.ReasonCode);

        Assert.Equal(collectionsBefore, collectionsAfter);
        Assert.Equal(linesBefore, linesAfter);
        var persistedTrip = await queryContext.TrmTrips.SingleAsync(x => x.Id == firstProjectedTrip.TripId);
        Assert.Equal(firstProjectedTrip.Fee, persistedTrip.Fee);
        Assert.Equal(firstProjectedTrip.OrNumber, persistedTrip.ORNumber);
    }
}
