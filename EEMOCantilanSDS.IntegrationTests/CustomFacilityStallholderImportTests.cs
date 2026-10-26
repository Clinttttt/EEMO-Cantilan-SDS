using EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The stallholder import into an office's own custom facility, through the real handler, repositories, tenant filters and PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomFacilityStallholderImportTests(PostgresFixture db)
{
    private sealed class QuietCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string t, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string t, FacilityCode f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string t, FacilityCode? f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Tenant : ITenantContext { public string TenantCode => "import-test"; }

    private static BulkImportStallholdersCommandHandler Handler(AppDbContext ctx) => new(
        new StallRepository(ctx), new FacilityRepository(ctx), new PayorRepository(ctx, null!), new UnitOfWork(ctx),
        new QuietCache(), new FeeRateResolver(ctx), new Tenant(), new SystemClock());

    private static ImportStallRow Row(int n, string occupant, string no, decimal rate, int years = 3, string effectivity = "2026-01-01") =>
        new(n, occupant, occupant, no, DateTime.Parse(effectivity), years, 12.5, rate, null, null);

    private async Task<Guid> SeedTenantAsync(string name, FacilityCode code, bool active = true,
        CustomFacilityCategory category = CustomFacilityCategory.StallRent)
    {
        var municipality = Municipality.Create($"CI-{Guid.NewGuid():N}"[..24], name, "Province", MunicipalityStatus.Active,
            tenantCode: $"ci-{Guid.NewGuid():N}"[..32]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        var facility = Facility.Create(code, $"{name} Hall", "HL", municipalityId: municipality.Id, customCategory: category);
        if (!active) facility.Deactivate();
        await using var ctx = db.CreateContext(municipality.Id);
        ctx.Facilities.Add(facility);
        await ctx.SaveChangesAsync();
        return municipality.Id;
    }

    [SkippableFact]
    public async Task ImportCreatesStallsAndContractsAtTheFileRates_RetryAddsNothing_AndNoMoneyIsRecorded()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom1);
        var rows = new List<ImportStallRow> { Row(1, "Ana Reyes", "A-1", 1234.56m, 2), Row(2, "Ben Cruz", "A-2", 777m, 5, "2024-03-01") };
        var command = new BulkImportStallholdersCommand(FacilityCode.Custom1, null, rows);

        await using (var ctx = db.CreateContext(tenant))
        {
            var result = await Handler(ctx).Handle(command, default);
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(2, result.Value!.CreatedCount);
        }

        await using (var ctx = db.CreateContext(tenant))
        {
            var stalls = await ctx.Stalls.Include(s => s.Contracts).Where(s => s.Facility!.Code == FacilityCode.Custom1).OrderBy(s => s.StallNo).ToListAsync();
            Assert.Equal(["A-1", "A-2"], stalls.Select(s => s.StallNo));
            Assert.Equal([1234.56m, 777m], stalls.Select(s => s.MonthlyRate));
            Assert.All(stalls, s => Assert.Null(s.Section));
            var a1 = Assert.Single(stalls[0].Contracts);
            Assert.Equal((new DateOnly(2026, 1, 1), 2, 1234.56m), (a1.EffectivityDate, a1.DurationYears, a1.MonthlyRentalRate));
            var a2 = Assert.Single(stalls[1].Contracts);
            Assert.Equal((new DateOnly(2024, 3, 1), 5), (a2.EffectivityDate, a2.DurationYears));
            Assert.Empty(await ctx.PaymentRecords.ToListAsync());   // an import records no money
        }

        // Retrying the same file: nothing new, every row reported as not imported.
        await using (var ctx = db.CreateContext(tenant))
        {
            var retry = await Handler(ctx).Handle(command, default);
            Assert.True(retry.IsSuccess, retry.Error);
            Assert.Equal((0, 0, 2), (retry.Value!.CreatedCount, retry.Value.RenewedCount, retry.Value.FailedCount));
        }
        await using (var ctx = db.CreateContext(tenant))
        {
            Assert.Equal(2, await ctx.Stalls.CountAsync());
            Assert.Equal(2, await ctx.Contracts.CountAsync());
        }
    }

    [SkippableFact]
    public async Task DuplicateStallNumberInTheFile_ReportsTheSecondRowAndSavesTheFirst()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.Custom2);
        await using var ctx = db.CreateContext(tenant);

        var result = await Handler(ctx).Handle(new BulkImportStallholdersCommand(FacilityCode.Custom2, null,
            [Row(1, "Ana Reyes", "7", 900m), Row(2, "Ben Cruz", "7", 950m)]), default);

        Assert.Equal((1, 1), (result.Value!.CreatedCount, result.Value.FailedCount));
        Assert.Contains("Duplicate stall number", result.Value.Results.Single(r => !r.Created).Error!);
    }

    [SkippableFact]
    public async Task AnotherTenantCannotImportIntoThisTenantsCustomFacility_AndHasItsOwnIndependently()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedTenantAsync("Alpha", FacilityCode.Custom1);
        var bWithout = await SeedTenantAsync("Bravo", FacilityCode.Custom2);   // Bravo has NO Custom1
        var bWith = await SeedTenantAsync("Charlie", FacilityCode.Custom1);

        await using (var ctx = db.CreateContext(bWithout))
        {
            var refused = await Handler(ctx).Handle(new BulkImportStallholdersCommand(FacilityCode.Custom1, null, [Row(1, "Ana", "1", 900m)]), default);
            Assert.False(refused.IsSuccess);
        }
        await using (var ctx = db.CreateContext(bWith))
            Assert.True((await Handler(ctx).Handle(new BulkImportStallholdersCommand(FacilityCode.Custom1, null, [Row(1, "Zed", "1", 321m)]), default)).IsSuccess);

        await using (var ctx = db.CreateContext(a))
            Assert.Empty(await ctx.Stalls.ToListAsync());   // Alpha untouched by the others
        await using (var ctx = db.CreateContext(Guid.Empty))
        {
            var all = await ctx.Stalls.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(bWith, Assert.Single(all).MunicipalityId);
        }
    }

    [SkippableFact]
    public async Task InactiveAndSpaceRentalCustomFacilities_AreRefusedAndNothingIsSaved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var inactive = await SeedTenantAsync("Alpha", FacilityCode.Custom1, active: false);
        var space = await SeedTenantAsync("Bravo", FacilityCode.Custom1, category: CustomFacilityCategory.SpaceRental);

        foreach (var (tenant, expected) in new[] { (inactive, "not active"), (space, "Space Rental") })
        {
            await using var ctx = db.CreateContext(tenant);
            var result = await Handler(ctx).Handle(new BulkImportStallholdersCommand(FacilityCode.Custom1, null, [Row(1, "Ana", "1", 900m)]), default);
            Assert.False(result.IsSuccess);
            Assert.Contains(expected, result.Error);
        }
        await using var check = db.CreateContext(Guid.Empty);
        Assert.Empty(await check.Stalls.IgnoreQueryFilters().ToListAsync());
    }

    [SkippableFact]
    public async Task BuiltInFacilityImportIsUnchanged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync("Alpha", FacilityCode.TCC);
        await using var ctx = db.CreateContext(tenant);

        var result = await Handler(ctx).Handle(new BulkImportStallholdersCommand(FacilityCode.TCC, null, [Row(1, "Ana", "1", 2760m)]), default);

        Assert.Equal(1, result.Value!.CreatedCount);
    }
}
