using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetTrmCollectionShadowReconciliation;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class GetTrmCollectionShadowReconciliationQueryHandlerTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 23);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid? municipalityId, bool authenticated = true) : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "trm-shadow-test";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"trm-shadow-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    private static AppDbContext Context(DbContextOptions<AppDbContext> options, Guid tenantId) =>
        new(options, new FixedMunicipality(tenantId));

    private static RevenueClassificationPolicy Policy(
        Guid classificationId,
        Guid tenantId,
        DateOnly effectiveDate,
        string? name = null) => RevenueClassificationPolicy.Create(
            classificationId,
            effectiveDate,
            name ?? $"Transportation policy {effectiveDate:yyyy-MM-dd}",
            RevenueInstrumentType.CashTicket,
            tenantId,
            createdBy: "seed");

    private static TrmTrip AddTrip(
        AppDbContext context,
        DateTime recordedAtUtc,
        decimal fee,
        Guid? collectorId = null,
        string? orNumber = null,
        string? plate = null,
        string? route = null)
    {
        var trip = TrmTrip.Create(
            transporterId: null,
            tripNumber: 1,
            driverName: "Walk-in driver",
            plateNumber: plate ?? "TRM 100",
            route: route ?? "Route A",
            orNumber: orNumber ?? $"LEGACY-OR-{Guid.NewGuid():N}"[..16],
            organization: "Unassociated",
            collectorId: collectorId,
            recordedAt: recordedAtUtc,
            fee: fee);
        context.TrmTrips.Add(trip);
        return trip;
    }

    private static GetTrmCollectionShadowReconciliationQueryHandler Handler(
        AppDbContext context,
        Guid? tenantId,
        bool authenticated = true) =>
        new(context, new Caller(tenantId, authenticated));

    [Fact]
    public async Task UnauthenticatedOrUnresolvedCallerIsForbidden()
    {
        var options = Options();
        await using var context = Context(options, Guid.Empty);
        var callers = new ICurrentUserService[]
        {
            new Caller(Guid.NewGuid(), authenticated: false),
            new Caller(null),
            new Caller(Guid.Empty)
        };

        foreach (var caller in callers)
        {
            var result = await new GetTrmCollectionShadowReconciliationQueryHandler(context, caller)
                .Handle(new(From, To), default);
            Assert.Equal(ResultStatus.Forbidden, result.Status);
        }
    }

    [Fact]
    public async Task ValidatorRejectsInvertedRangeAndAllowsInclusiveRange()
    {
        var validator = new GetTrmCollectionShadowReconciliationQueryValidator();
        var invalid = await validator.ValidateAsync(new GetTrmCollectionShadowReconciliationQuery(To, From));

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, x => x.PropertyName == nameof(GetTrmCollectionShadowReconciliationQuery.To));
        Assert.True((await validator.ValidateAsync(new GetTrmCollectionShadowReconciliationQuery(From, From))).IsValid);
        Assert.True((await validator.ValidateAsync(new GetTrmCollectionShadowReconciliationQuery(From, To))).IsValid);
    }

    [Fact]
    public async Task PersistedTripFeesProjectExactlyOnceUsingHistoricalPolicyAndPhilippineBusinessDate()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        var collectorId = Guid.NewGuid();
        Guid classificationId;
        Guid olderPolicyId;
        Guid winningTiePolicyId;
        Guid firstTripId;
        Guid secondTripId;
        Guid thirdTripId;

        await using (var seed = Context(options, tenantId))
        {
            var classification = RevenueClassification.Create(
                RevenueClassificationCodes.TransportationParking, tenantId, "seed");
            classification.Retire("seed-retired");
            classificationId = classification.Id;

            var olderPolicy = Policy(classification.Id, tenantId, new DateOnly(2026, 9, 1));
            var tiePolicyA = Policy(classification.Id, tenantId, new DateOnly(2026, 9, 20), "Tie A");
            var tiePolicyB = Policy(classification.Id, tenantId, new DateOnly(2026, 9, 20), "Tie B");
            var futurePolicy = Policy(classification.Id, tenantId, new DateOnly(2026, 10, 1), "Future only");
            olderPolicyId = olderPolicy.Id;
            winningTiePolicyId = new[] { tiePolicyA, tiePolicyB }.MaxBy(x => x.Id)!.Id;

            var firstTrip = AddTrip(seed,
                DateTime.SpecifyKind(new DateTime(2026, 9, 18, 16, 0, 0), DateTimeKind.Utc),
                30.17m, collectorId, "OLD-OR-1", "TRI 001", "Airport route"); // 19 Sep PHT
            var secondTrip = AddTrip(seed,
                DateTime.SpecifyKind(new DateTime(2026, 9, 20, 16, 0, 0), DateTimeKind.Utc),
                47.25m, null, "OLD-OR-2", "BUS 001", "Town loop"); // 21 Sep PHT
            var thirdTrip = AddTrip(seed,
                DateTime.SpecifyKind(new DateTime(2026, 9, 22, 16, 0, 0), DateTimeKind.Utc),
                61.72m, null, "OLD-OR-3", "VAN 001", "Highway route"); // 23 Sep PHT
            firstTripId = firstTrip.Id;
            secondTripId = secondTrip.Id;
            thirdTripId = thirdTrip.Id;
            seed.AddRange(classification, olderPolicy, tiePolicyA, tiePolicyB, futurePolicy);
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenantId);
        var collectionsBefore = await context.Collections.CountAsync();
        var linesBefore = await context.CollectionLines.CountAsync();
        var result = await Handler(context, tenantId).Handle(new(From, To), default);
        var collectionsAfter = await context.Collections.CountAsync();
        var linesAfter = await context.CollectionLines.CountAsync();

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(3, reconciliation.SourceCount);
        Assert.Equal(3, reconciliation.ProjectedCount);
        Assert.Equal(0, reconciliation.UnresolvedCount);
        Assert.Equal(139.14m, reconciliation.SourceTotal);
        Assert.Equal(reconciliation.SourceTotal, reconciliation.ProjectedTotal);
        Assert.Equal(0m, reconciliation.UnresolvedTotal);
        Assert.Equal(0m, reconciliation.Difference);
        Assert.True(reconciliation.IsReconciled);

        var rows = reconciliation.ProjectedRows;
        Assert.Equal(new[] { firstTripId, secondTripId, thirdTripId }, rows.Select(x => x.TrmTripId));
        var first = rows[0];
        Assert.Equal(new DateOnly(2026, 9, 19), first.BusinessDate);
        Assert.Equal(30.17m, first.Amount);
        Assert.Equal(collectorId, first.CollectorId);
        Assert.Equal(classificationId, first.RevenueClassificationId);
        Assert.Equal(olderPolicyId, first.RevenueClassificationPolicyId);
        Assert.Equal(CollectionSourceKind.TrmTrip, first.SourceKind);
        Assert.Equal(firstTripId, first.SourceId);

        var second = rows[1];
        Assert.Equal(new DateOnly(2026, 9, 21), second.BusinessDate);
        Assert.Equal(47.25m, second.Amount);
        Assert.Equal(winningTiePolicyId, second.RevenueClassificationPolicyId);
        Assert.Equal(new DateOnly(2026, 9, 20), second.PolicyEffectiveDate);

        var third = rows[2];
        Assert.Equal(new DateOnly(2026, 9, 23), third.BusinessDate);
        Assert.Equal(61.72m, third.Amount);
        Assert.Equal(winningTiePolicyId, third.RevenueClassificationPolicyId);

        // The type has no reliable vehicle-class field; no vehicle inference or re-rating is part of this projection.
        Assert.Null(typeof(TrmTrip).GetProperty("VehicleClass"));
        Assert.Equal(collectionsBefore, collectionsAfter);
        Assert.Equal(linesBefore, linesAfter);
    }

    [Fact]
    public async Task MissingTransportationParkingClassificationLeavesEveryTripUnresolved()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        await using (var seed = Context(options, tenantId))
        {
            AddTrip(seed, DateTime.SpecifyKind(new DateTime(2026, 9, 18, 16, 0, 0), DateTimeKind.Utc), 25.50m);
            AddTrip(seed, DateTime.SpecifyKind(new DateTime(2026, 9, 19, 16, 0, 0), DateTimeKind.Utc), 31.25m);
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenantId);
        var result = await Handler(context, tenantId).Handle(new(From, To), default);

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(2, reconciliation.SourceCount);
        Assert.Empty(reconciliation.ProjectedRows);
        Assert.Equal(2, reconciliation.UnresolvedCount);
        Assert.Equal(56.75m, reconciliation.SourceTotal);
        Assert.Equal(0m, reconciliation.ProjectedTotal);
        Assert.Equal(56.75m, reconciliation.UnresolvedTotal);
        Assert.Equal(56.75m, reconciliation.Difference);
        Assert.False(reconciliation.IsReconciled);
        Assert.All(reconciliation.UnresolvedRows, row =>
            Assert.Equal(TrmCollectionShadowUnresolvedReasons.TransportationParkingClassificationMissingCode, row.ReasonCode));
        Assert.Equal(reconciliation.SourceCount, reconciliation.ProjectedCount + reconciliation.UnresolvedCount);
        Assert.Equal(reconciliation.SourceTotal, reconciliation.ProjectedTotal + reconciliation.UnresolvedTotal);
    }

    [Fact]
    public async Task FutureOnlyPolicyIsNotAppliedToHistoricalTrip()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        Guid tripId;
        await using (var seed = Context(options, tenantId))
        {
            var classification = RevenueClassification.Create(
                RevenueClassificationCodes.TransportationParking, tenantId, "seed");
            seed.RevenueClassifications.Add(classification);
            seed.RevenueClassificationPolicies.Add(Policy(classification.Id, tenantId, new DateOnly(2026, 10, 1)));
            tripId = AddTrip(seed,
                DateTime.SpecifyKind(new DateTime(2026, 9, 22, 16, 0, 0), DateTimeKind.Utc), 88.88m).Id;
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenantId);
        var result = await Handler(context, tenantId).Handle(new(From, To), default);

        Assert.True(result.IsSuccess);
        var reconciliation = result.Value!;
        Assert.Equal(1, reconciliation.SourceCount);
        Assert.Empty(reconciliation.ProjectedRows);
        var unresolved = Assert.Single(reconciliation.UnresolvedRows);
        Assert.Equal(tripId, unresolved.TrmTripId);
        Assert.Equal(new DateOnly(2026, 9, 23), unresolved.BusinessDate);
        Assert.Equal(88.88m, unresolved.Amount);
        Assert.Equal(TrmCollectionShadowUnresolvedReasons.TransportationParkingPolicyNotEffectiveCode, unresolved.ReasonCode);
        Assert.Equal(88.88m, reconciliation.Difference);
        Assert.False(reconciliation.IsReconciled);
    }

    [Fact]
    public async Task TenantFilterExcludesOtherMunicipalityTripMoney()
    {
        var options = Options();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var seedA = Context(options, tenantA))
        {
            var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, tenantA, "seed");
            seedA.AddRange(classification, Policy(classification.Id, tenantA, new DateOnly(2026, 9, 1)));
            AddTrip(seedA, DateTime.SpecifyKind(new DateTime(2026, 9, 22, 16, 0, 0), DateTimeKind.Utc), 35m);
            await seedA.SaveChangesAsync();
        }
        await using (var seedB = Context(options, tenantB))
        {
            var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, tenantB, "seed");
            seedB.AddRange(classification, Policy(classification.Id, tenantB, new DateOnly(2026, 9, 1)));
            AddTrip(seedB, DateTime.SpecifyKind(new DateTime(2026, 9, 22, 16, 0, 0), DateTimeKind.Utc), 999.99m);
            await seedB.SaveChangesAsync();
        }

        await using var context = Context(options, tenantA);
        var result = await Handler(context, tenantA).Handle(new(From, To), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.SourceCount);
        Assert.Equal(35m, result.Value.SourceTotal);
        Assert.Equal(35m, result.Value.ProjectedTotal);
        Assert.DoesNotContain(result.Value.ProjectedRows, x => x.Amount == 999.99m);
    }

    [Fact]
    public async Task PhilippineDayWindowIsInclusiveAndEmptyWindowReconcilesToZero()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        await using (var seed = Context(options, tenantId))
        {
            var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, tenantId, "seed");
            seed.AddRange(classification, Policy(classification.Id, tenantId, new DateOnly(2026, 9, 1)));
            AddTrip(seed, DateTime.SpecifyKind(new DateTime(2026, 9, 22, 16, 0, 0), DateTimeKind.Utc), 10m); // 23 Sep 00:00 PHT
            AddTrip(seed, DateTime.SpecifyKind(new DateTime(2026, 9, 23, 15, 59, 59), DateTimeKind.Utc), 20m); // 23 Sep 23:59:59 PHT
            AddTrip(seed, DateTime.SpecifyKind(new DateTime(2026, 9, 23, 16, 0, 0), DateTimeKind.Utc), 30m); // 24 Sep 00:00 PHT
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenantId);
        var oneDay = await Handler(context, tenantId).Handle(new(new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 23)), default);
        Assert.True(oneDay.IsSuccess);
        Assert.Equal(2, oneDay.Value!.SourceCount);
        Assert.Equal(30m, oneDay.Value.SourceTotal);
        Assert.All(oneDay.Value.ProjectedRows, x => Assert.Equal(new DateOnly(2026, 9, 23), x.BusinessDate));

        var empty = await Handler(context, tenantId).Handle(new(new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 25)), default);
        Assert.True(empty.IsSuccess);
        Assert.Equal(0, empty.Value!.SourceCount);
        Assert.Equal(0m, empty.Value.SourceTotal);
        Assert.True(empty.Value.IsReconciled);
    }
}
