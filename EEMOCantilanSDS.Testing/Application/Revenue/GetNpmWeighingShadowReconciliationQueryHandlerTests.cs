using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetNpmWeighingShadowReconciliation;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

/// <summary>
/// NPM weighing → WEIGHT_AND_MEASURE shadow: frozen Meat weighing money only, Fish weighing unresolved with kilos and
/// no inferred amount, daily stall rent never included, tenant-isolated and write-free.
/// </summary>
public sealed class GetNpmWeighingShadowReconciliationQueryHandlerTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);
    private static readonly DateOnly RateDate = new(2026, 9, 29);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid? municipalityId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "weighing-shadow-test";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"weighing-shadow-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    private static AppDbContext Context(DbContextOptions<AppDbContext> options, Guid tenantId) =>
        new(options, new FixedMunicipality(tenantId));

    private static DailyCollection Paid(DateOnly date, decimal? fishKilos = null, decimal? meatKilos = null)
    {
        var row = DailyCollection.Create(Guid.NewGuid(), date, dailyFee: 30m);
        row.MarkPaid("OR-1", Guid.NewGuid(), fishKilos, meatKilos: meatKilos,
            meatFeeRatePerKilo: meatKilos.HasValue ? 66m : null,
            meatFeeRateEffectiveDate: meatKilos.HasValue ? RateDate : null);
        return row;
    }

    private static async Task SeedPolicyAsync(DbContextOptions<AppDbContext> options, Guid tenantId, DateOnly effective)
    {
        await using var context = Context(options, tenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, tenantId);
        context.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, effective,
            "Weight & Measure", RevenueInstrumentType.OfficialReceipt, tenantId));
        await context.SaveChangesAsync();
    }

    private static async Task<Result<NpmWeighingShadowReconciliationDto>> RunAsync(
        DbContextOptions<AppDbContext> options, Guid tenantId, Guid? callerTenant = null)
    {
        await using var context = Context(options, tenantId);
        return await new GetNpmWeighingShadowReconciliationQueryHandler(context, new Caller(callerTenant ?? tenantId))
            .Handle(new GetNpmWeighingShadowReconciliationQuery(From, To), default);
    }

    [Fact]
    public async Task FrozenMeatWeighingProjectsToWeightAndMeasureWithoutStallRent()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        await SeedPolicyAsync(options, tenantId, new DateOnly(2020, 1, 1));
        var meat = Paid(new DateOnly(2026, 9, 29), meatKilos: 2.5m);
        var rentOnly = Paid(new DateOnly(2026, 9, 29));
        await using (var context = Context(options, tenantId))
        {
            context.AddRange(meat, rentOnly);
            await context.SaveChangesAsync();
        }

        var result = await RunAsync(options, tenantId);

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value!;
        var row = Assert.Single(dto.ProjectedRows);
        Assert.Equal(meat.Id, row.SourceId);
        Assert.Equal(CollectionSourceKind.DailyCollection, row.SourceKind);
        Assert.Equal(CollectionSourcePart.MeatWeighing, row.SourcePart);
        Assert.Equal(165m, row.Amount); // frozen 2.5 kg × ₱66, never the ₱30 daily stall rent
        Assert.Equal(66m, row.RatePerKilo);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, row.PolicyInstrument);
        Assert.Equal(165m, dto.FrozenSourceTotal);
        Assert.True(dto.IsReconciled);
    }

    [Fact]
    public async Task FishWeighingStaysUnresolvedWithKilosAndNoInferredAmount()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        await SeedPolicyAsync(options, tenantId, new DateOnly(2020, 1, 1));
        await using (var context = Context(options, tenantId))
        {
            context.Add(Paid(new DateOnly(2026, 9, 10), fishKilos: 12m));
            await context.SaveChangesAsync();
        }

        var dto = (await RunAsync(options, tenantId)).Value!;

        Assert.Empty(dto.ProjectedRows);
        var unresolved = Assert.Single(dto.UnresolvedRows);
        Assert.Equal(NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenCode, unresolved.ReasonCode);
        Assert.Equal(CollectionSourcePart.FishFee, unresolved.SourcePart);
        Assert.Equal(12m, unresolved.Kilos);
        Assert.Null(unresolved.Amount);
        Assert.Equal(12m, dto.UnresolvedFishKilos);
        Assert.Equal(0m, dto.FrozenSourceTotal);
        Assert.False(dto.IsReconciled);
    }

    [Fact]
    public async Task FishWeighingWithFrozenEvidenceProjectsWhileHistoricalFishStaysUnresolved()
    {
        var options = Options();
        var tenantId = Guid.NewGuid();
        await SeedPolicyAsync(options, tenantId, new DateOnly(2020, 1, 1));
        var frozen = DailyCollection.Create(Guid.NewGuid(), new DateOnly(2026, 9, 30), dailyFee: 30m);
        frozen.MarkPaid("OR-2", Guid.NewGuid(), 10m, fishFeeRatePerKilo: 1.5m, fishFeeRateEffectiveDate: new DateOnly(2020, 1, 1));
        var historical = Paid(new DateOnly(2026, 9, 10), fishKilos: 12m); // kilos only, no frozen rate
        await using (var context = Context(options, tenantId))
        {
            context.AddRange(frozen, historical);
            await context.SaveChangesAsync();
        }

        var dto = (await RunAsync(options, tenantId)).Value!;

        var projected = Assert.Single(dto.ProjectedRows);
        Assert.Equal(frozen.Id, projected.SourceId);
        Assert.Equal(CollectionSourcePart.FishFee, projected.SourcePart);
        Assert.Equal(15m, projected.Amount);                  // frozen 10 kg x 1.50, whatever the rate is today
        Assert.Equal(new DateOnly(2020, 1, 1), projected.RateEffectiveDate);
        var unresolved = Assert.Single(dto.UnresolvedRows);
        Assert.Equal(historical.Id, unresolved.DailyCollectionId);
        Assert.Equal(NpmWeighingShadowUnresolvedReasons.FishRateNotFrozenCode, unresolved.ReasonCode);
        Assert.Null(unresolved.Amount);
        Assert.Equal(12m, dto.UnresolvedFishKilos);           // only the row with no evidence counts as unresolved kilos
        Assert.Equal(15m, dto.FrozenSourceTotal);
    }

    [Fact]
    public async Task MissingClassificationOrPolicyLeavesFrozenMoneyUnresolved()
    {
        var noClassification = Options();
        var tenantA = Guid.NewGuid();
        var laterPolicy = Options();
        var tenantB = Guid.NewGuid();
        await SeedPolicyAsync(laterPolicy, tenantB, new DateOnly(2026, 10, 1));
        foreach (var (options, tenant) in new[] { (noClassification, tenantA), (laterPolicy, tenantB) })
        {
            await using var context = Context(options, tenant);
            context.Add(Paid(new DateOnly(2026, 9, 29), meatKilos: 1m));
            await context.SaveChangesAsync();
        }

        var a = (await RunAsync(noClassification, tenantA)).Value!;
        var b = (await RunAsync(laterPolicy, tenantB)).Value!;

        Assert.Equal(NpmWeighingShadowUnresolvedReasons.ClassificationMissingCode, Assert.Single(a.UnresolvedRows).ReasonCode);
        Assert.Equal(NpmWeighingShadowUnresolvedReasons.PolicyNotEffectiveCode, Assert.Single(b.UnresolvedRows).ReasonCode);
        Assert.Equal(66m, a.Difference);
        Assert.Empty(b.ProjectedRows);
    }

    [Fact]
    public async Task AnotherTenantsWeighingIsNotCountedAndNothingIsWritten()
    {
        var options = Options();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedPolicyAsync(options, tenantA, new DateOnly(2020, 1, 1));
        await using (var context = Context(options, tenantB))
        {
            context.Add(Paid(new DateOnly(2026, 9, 29), meatKilos: 10m));
            await context.SaveChangesAsync();
        }

        var dto = (await RunAsync(options, tenantA)).Value!;

        Assert.Empty(dto.ProjectedRows);
        Assert.Empty(dto.UnresolvedRows);
        await using var verify = Context(options, tenantA);
        Assert.Empty(await verify.Collections.ToListAsync());
        Assert.Empty(await verify.CollectionLines.ToListAsync());
        Assert.Equal(ResultStatus.Forbidden, (await RunAsync(options, tenantA, Guid.Empty)).Status);
    }
}
