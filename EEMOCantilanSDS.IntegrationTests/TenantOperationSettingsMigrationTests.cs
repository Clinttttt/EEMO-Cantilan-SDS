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
using Npgsql;

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

    private sealed class NoTenant : EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => Guid.Empty;
        public void Set(Guid municipalityId) { }
    }

    /// <summary>
    /// A schema is rewound by building a SEPARATE throwaway database forward to the wanted migration, never by running later migrations'
    /// Down. Some later migrations are deliberately forward-only (the NPM evidence schema refuses Down with "An approved forward recovery
    /// plan is required"); that safeguard stays, and these tests no longer depend on crossing it.
    /// </summary>
    private async Task<(string Name, string ConnectionString)> NewThrowawayDatabaseAsync()
    {
        var name = "ops_" + Guid.NewGuid().ToString("N")[..10];
        await using var admin = db.CreateRawConnection();
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(db.CreateRawConnection().ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        return (name, cs);
    }

    private async Task DropAsync(string name)
    {
        await using var admin = db.CreateRawConnection();
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static Microsoft.EntityFrameworkCore.DbContext Open(string cs) =>
        new EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext(
            new DbContextOptionsBuilder<EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext>().UseNpgsql(cs).Options, new NoTenant());

    private static EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext OpenApp(string cs) =>
        (EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext)Open(cs);

    [SkippableFact]
    public async Task Upgrading_AnExistingSchemaWithData_LeavesItUntouched_AndTheTenantOnTheLegacyFallback()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        Guid cantilan;
        var (dbName, cs) = await NewThrowawayDatabaseAsync();
        try
        {
            // The schema as it was before this migration - what production holds today - built forward in its own database.
            await using (var rewind = Open(cs)) await MigrateToAsync(rewind, PreviousMigration);
            await using (var check = Open(cs))
                Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
                    "select count(*)::int as \"Value\" from information_schema.tables where table_name = 'TenantOperationSettings'").SingleAsync());

            var municipality = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            cantilan = municipality.Id;
            await using (var seed = OpenApp(cs))
            {
                seed.Municipalities.Add(municipality);
                await seed.SaveChangesAsync();

                // Seed a genuinely OLD Facilities row. The latest EF model includes later-added
                // CustomCategory, which this pre-2026-10-12 schema intentionally lacks.
                // Inserting a latest-model Facility via SaveChanges would therefore fail before
                // this test ever reaches the migration it is meant to verify.
                await seed.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Facilities"
                        ("Id", "MunicipalityId", "Code", "Archetype", "Name", "ShortName",
                         "IsActive", "CreatedAt", "CreatedBy", "IsDeleted")
                    VALUES
                        ({Guid.NewGuid()}, {cantilan}, {(int)FacilityCode.NPM},
                         {(int)BillingArchetype.DailyStall}, {"New Public Market"}, {"NPM"},
                         {true}, {DateTime.UtcNow}, {"System"}, {false})
                    """);

                seed.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2020, 1, 1), cantilan, "seed"));
                seed.AdminUsers.Add(AdminUser.Create("Head", "head", "head@x.ph", new HashedPassword("h"), AdminRole.SuperAdmin, cantilan, mustChangePassword: false));
                await seed.SaveChangesAsync();
            }

            // Upgrade, twice (the second is a no-op).
            await using (var up = Open(cs)) await up.Database.MigrateAsync();
            await using (var again = Open(cs)) await again.Database.MigrateAsync();

            await using var verify = OpenApp(cs);
            Assert.Contains(ThisMigration, await verify.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, (await verify.Database.GetAppliedMigrationsAsync()).Count(m => m == ThisMigration));
            Assert.Equal(1, await verify.Facilities.IgnoreQueryFilters().CountAsync());
            Assert.Equal(CustomFacilityCategory.StallRent,
                (await verify.Facilities.IgnoreQueryFilters().SingleAsync()).CustomCategory);
            var rate = await verify.FacilityRates.IgnoreQueryFilters().SingleAsync(r => r.MunicipalityId == cantilan);
            Assert.Equal(30m, rate.Amount);
            Assert.Equal(1, await verify.AdminUsers.IgnoreQueryFilters().CountAsync());

            // Nothing was seeded into the new table, so the existing municipality is read as it always was.
            Assert.Equal(0, await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync());
            var dto = await TenantOperationSettingsStore.ReadAsync(verify, cantilan, CancellationToken.None);
            Assert.False(dto.UsesConfiguration);
            Assert.All(dto.Items, i => Assert.True(i.IsListed));
            Assert.Equal("Kanmanggay", dto.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").DisplayName);
        }
        finally { await DropAsync(dbName); }
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
        var (dbName, cs) = await NewThrowawayDatabaseAsync();
        try
        {
            // Forward to THIS migration, then undo only it: its own Down is what is under test, and no later migration is crossed.
            await using (var forward = Open(cs)) await MigrateToAsync(forward, ThisMigration);
            await using (var before = Open(cs))
                Assert.Equal(1, await before.Database.SqlQueryRaw<int>(
                    "select count(*)::int as \"Value\" from information_schema.tables where table_name = 'TenantOperationSettings'").SingleAsync());
            await using (var rewind = Open(cs)) await MigrateToAsync(rewind, PreviousMigration);
            await using var check = Open(cs);
            var tables = await check.Database.SqlQueryRaw<string>(
                "select table_name as \"Value\" from information_schema.tables where table_schema = 'public' and table_name in ('TenantOperationSettings','Facilities','Municipalities')").ToListAsync();
            Assert.DoesNotContain("TenantOperationSettings", tables);
            Assert.Contains("Facilities", tables);
            Assert.Contains("Municipalities", tables);
        }
        finally { await DropAsync(dbName); }
    }
}
