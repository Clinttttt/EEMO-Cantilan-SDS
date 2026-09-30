using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetNpmWeighingShadowReconciliation;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class NpmWeighingShadowReconciliationTests(PostgresFixture db)
{
    private sealed class Caller(Guid municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => null;
        public string? Username => "weighing-shadow-integration";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    private async Task<Guid> SeedAsync(string code, decimal meatKilos, decimal fishKilos)
    {
        var municipality = Municipality.Create(code, code, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }
        var tenantId = municipality.Id;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenantId);
        var stall = Stall.Create(facility.Id, "M-01", 0m, ApplicableFees.DailyRental, MarketSection.MeatSection,
            createdBy: "test", municipalityId: tenantId);
        var meat = DailyCollection.Create(stall.Id, new DateOnly(2026, 9, 29), dailyFee: 30m);
        meat.MarkPaid("OR-M", null, meatKilos: meatKilos, meatFeeRatePerKilo: 66m,
            meatFeeRateEffectiveDate: new DateOnly(2026, 9, 29));
        var fish = DailyCollection.Create(stall.Id, new DateOnly(2026, 9, 28), dailyFee: 30m);
        fish.MarkPaid("OR-F", null, fishKilos);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, tenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2020, 1, 1),
            "Weight & Measure", RevenueInstrumentType.OfficialReceipt, tenantId);
        await using var context = db.CreateContext(tenantId);
        context.AddRange(facility, stall, meat, fish, classification, policy);
        await context.SaveChangesAsync();
        return tenantId;
    }

    [SkippableFact]
    public async Task PostgreSqlWeighingShadowIsTenantIsolatedAndWritesNoLedger()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = await SeedAsync($"WM-A-{suffix}", 2m, 5m);
        await SeedAsync($"WM-B-{suffix}", 100m, 50m);

        await using var context = db.CreateContext(tenantA);
        var result = await new GetNpmWeighingShadowReconciliationQueryHandler(context, new Caller(tenantA))
            .Handle(new GetNpmWeighingShadowReconciliationQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), default);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(result.Value!.ProjectedRows);
        Assert.Equal(132m, row.Amount);
        Assert.Equal(CollectionSourcePart.MeatWeighing, row.SourcePart);
        var fish = Assert.Single(result.Value.UnresolvedRows);
        Assert.Equal(NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenCode, fish.ReasonCode);
        Assert.Equal(5m, result.Value.UnresolvedFishKilos);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Empty(await context.PostingOperations.ToListAsync());
    }
}
