using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The tenant operation configuration migration against a real PostgreSQL: it is additive, it leaves an existing municipality's data
/// untouched, an existing municipality reads on the legacy fallback afterwards (never as "nothing enabled"), it can be applied twice,
/// and the table enforces one setting per municipality and operation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantOperationSettingsMigrationTests(PostgresFixture db)
{
    private const string PreviousMigration = "20261011144108_NpmPartialInstallments20261012";
    private const string ThisMigration = "20261012074910_TenantOperationSettings20261012";

    private static async Task MigrateToAsync(Microsoft.EntityFrameworkCore.DbContext context, string? target) =>
        await context.GetService<IMigrator>().MigrateAsync(target);

    [SkippableFact]
    public async Task Upgrading_AnExistingSchemaWithData_LeavesItUntouched_AndTheTenantOnTheLegacyFallback()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        Guid cantilan;
        try
        {
            // The schema as it was before this migration - what production holds today.
            await using (var rewind = db.CreateContext(Guid.Empty)) await MigrateToAsync(rewind, PreviousMigration);
            await using (var check = db.CreateContext(Guid.Empty))
                Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
                    "select count(*)::int as \"Value\" from information_schema.tables where table_name = 'TenantOperationSettings'").SingleAsync());

            var municipality = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            cantilan = municipality.Id;
            await using (var seed = db.CreateContext(Guid.Empty))
            {
                seed.Municipalities.Add(municipality);
                seed.Facilities.Add(Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", archetype: BillingArchetype.DailyStall, municipalityId: cantilan));
                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2020, 1, 1), cantilan, "seed"));
                seed.AdminUsers.Add(AdminUser.Create("Head", "head", "head@x.ph", new HashedPassword("h"), AdminRole.SuperAdmin, cantilan, mustChangePassword: false));
                await seed.SaveChangesAsync();
            }

            // Upgrade, twice (the second is a no-op).
            await using (var up = db.CreateContext(Guid.Empty)) await up.Database.MigrateAsync();
            await using (var again = db.CreateContext(Guid.Empty)) await again.Database.MigrateAsync();

            await using var verify = db.CreateContext(Guid.Empty);
            Assert.Contains(ThisMigration, await verify.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, (await verify.Database.GetAppliedMigrationsAsync()).Count(m => m == ThisMigration));
            Assert.Equal(1, await verify.Facilities.IgnoreQueryFilters().CountAsync());
            var rate = await verify.FacilityRates.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(30m, rate.Amount);
            Assert.Equal(1, await verify.AdminUsers.IgnoreQueryFilters().CountAsync());

            // Nothing was seeded into the new table, so the existing municipality is read as it always was.
            Assert.Equal(0, await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync());
            var dto = await TenantOperationSettingsStore.ReadAsync(verify, cantilan, CancellationToken.None);
            Assert.False(dto.UsesConfiguration);
            Assert.All(dto.Items, i => Assert.True(i.IsListed));
            Assert.Equal("Kanmanggay", dto.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").DisplayName);
        }
        finally
        {
            await using var restore = db.CreateContext(Guid.Empty);
            await restore.Database.MigrateAsync();
        }
    }

    [SkippableFact]
    public async Task TheTable_EnforcesOneSettingPerMunicipalityAndOperation_AtTheDatabaseLevel()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        await using (var up = db.CreateContext(Guid.Empty)) await up.Database.MigrateAsync();

        await using var ctx = db.CreateContext(Guid.Empty);
        var rows = await ctx.Database.SqlQueryRaw<string>(
            "select indexdef as \"Value\" from pg_indexes where tablename = 'TenantOperationSettings' and indexname like 'IX_%MunicipalityId_OperationCode'").ToListAsync();
        var index = Assert.Single(rows);
        Assert.Contains("UNIQUE", index);
        Assert.Contains("\"MunicipalityId\", \"OperationCode\"", index);
    }

    [SkippableFact]
    public async Task RollingBackThisMigration_DropsOnlyItsOwnTable()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        try
        {
            await using (var rewind = db.CreateContext(Guid.Empty)) await MigrateToAsync(rewind, PreviousMigration);
            await using var check = db.CreateContext(Guid.Empty);
            var tables = await check.Database.SqlQueryRaw<string>(
                "select table_name as \"Value\" from information_schema.tables where table_schema = 'public' and table_name in ('TenantOperationSettings','Facilities','Municipalities')").ToListAsync();
            Assert.DoesNotContain("TenantOperationSettings", tables);
            Assert.Contains("Facilities", tables);
            Assert.Contains("Municipalities", tables);
        }
        finally
        {
            await using var restore = db.CreateContext(Guid.Empty);
            await restore.Database.MigrateAsync();
        }
    }
}
