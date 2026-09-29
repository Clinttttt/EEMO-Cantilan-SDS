using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CanonicalMonthlyIncomeReaderTests(PostgresFixture db)
{
    private static readonly DateTime Now = new(2026, 10, 3, 4, 0, 0, DateTimeKind.Utc);

    private sealed class Caller(Guid municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string? Username => "canonical-income-integration";
        public string? Role => "Admin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class StoppedClock : IClock
    {
        public DateTime UtcNow => Now;
        public DateTime PhilippineNow => DateTime.SpecifyKind(Now.AddHours(8), DateTimeKind.Unspecified);
        public DateOnly PhilippineToday => DateOnly.FromDateTime(PhilippineNow);
    }

    private async Task<Guid> SeedTenantAsync(string code, decimal amount, bool reverse)
    {
        var municipality = Municipality.Create(code, code, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var tenantId = municipality.Id;
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2020, 1, 1),
            "Water Consumption Fee", RevenueInstrumentType.CashTicket, tenantId);
        var sourceId = Guid.NewGuid();
        var collection = Collection.Post(new DateOnly(2026, 9, 26), new DateTime(2026, 9, 26, 2, 0, 0, DateTimeKind.Utc),
            "collector", "Collector", "Collector",
            [new CollectionLineDraft(classification, policy, amount, CollectionSourceKind.UtilityBill, sourceId,
                CollectionSourcePart.Water, null,
                [new CollectionAllocationDraft(CollectionSourceKind.UtilityBill, sourceId, amount, CollectionSourcePart.Water)])]);
        await using var context = db.CreateContext(tenantId);
        context.AddRange(classification, policy, collection);
        if (reverse)
            context.Add(CollectionCorrection.Record(tenantId, collection.Id, null, null, null,
                CollectionCorrectionType.Reversal, new DateOnly(2026, 9, 27),
                new DateTime(2026, 9, 27, 1, 0, 0, DateTimeKind.Utc), -amount, "integration reversal",
                "head", "Head", [new CollectionCorrectionLineDraft(collection.Lines.Single().Id, -amount,
                    [new CollectionCorrectionAllocationDraft(collection.Lines.Single().Allocations.Single().Id, -amount)])]));
        await context.SaveChangesAsync();
        return tenantId;
    }

    private async Task<CanonicalMonthlyIncomeDto> RunAsync(Guid tenantId, CanonicalReportingBasis basis, DateTimeOffset? asOf = null)
    {
        await using var context = db.CreateContext(tenantId);
        var result = await new GetCanonicalMonthlyIncomeQueryHandler(context, new Caller(tenantId),
                new FixedTenant(tenantId), new StoppedClock())
            .Handle(new GetCanonicalMonthlyIncomeQuery(2026, 9, basis, asOf), default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [SkippableFact]
    public async Task PostgreSqlReaderIsTenantIsolatedAppliesKnowledgeCutoffAndWritesNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = await SeedTenantAsync($"INC-A-{suffix}", 300m, reverse: true);
        var tenantB = await SeedTenantAsync($"INC-B-{suffix}", 7000m, reverse: false);

        var asOf = await RunAsync(tenantA, CanonicalReportingBasis.AsOf,
            new DateTimeOffset(2026, 9, 26, 23, 59, 0, TimeSpan.FromHours(8)));
        var latest = await RunAsync(tenantA, CanonicalReportingBasis.LatestCorrected);
        var other = await RunAsync(tenantB, CanonicalReportingBasis.LatestCorrected);

        Assert.Equal(300m, asOf.GrandTotal.NetCollected);
        Assert.Equal(0m, asOf.GrandTotal.CorrectionEffect);
        Assert.Equal(300m, latest.GrandTotal.GrossOriginalCollected); // allocations are not added again
        Assert.Equal(-300m, latest.GrandTotal.CorrectionEffect);
        Assert.Equal(0m, latest.GrandTotal.NetCollected);
        var row = Assert.Single(latest.Rows);
        Assert.Equal(RevenueClassificationCodes.Wcf, row.SemanticCode);
        Assert.Equal("Water Consumption Fee", row.DisplayName);
        Assert.Equal(7000m, other.GrandTotal.NetCollected);
        Assert.Equal(0, other.CorrectionCount);

        await using var verify = db.CreateContext(tenantA);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.Single(await verify.CollectionCorrections.ToListAsync());
        Assert.Empty(await verify.PostingOperations.ToListAsync());
    }
}
