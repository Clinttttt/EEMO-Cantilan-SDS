using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class FineAssessmentMigrationCompatibilityTests(PostgresFixture database)
{
    private const string FineMigration = "20261010013040_GovernedFineAssessments20261010";

    [SkippableTheory]
    [InlineData("constraint")]
    [InlineData("index")]
    [InlineData("absent")]
    [InlineData("wrong-index")]
    [InlineData("wrong-constraint")]
    public async Task Pending_fine_migration_reuses_only_the_verified_contract_tenant_key(string existing)
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "");
        await using var admin = database.CreateRawConnection();
        var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Pooling = false };
        await admin.OpenAsync();
        var name = "fine_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.Database = name;
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var prior = db.Database.GetMigrations().Where(x => string.CompareOrdinal(x, FineMigration) < 0).Last();
            await db.GetService<IMigrator>().MigrateAsync(prior);
            if (existing == "constraint") await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Contracts\" ADD CONSTRAINT \"AK_Contracts_MunicipalityId_Id\" UNIQUE (\"MunicipalityId\", \"Id\")");
            if (existing == "index") await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX \"AK_Contracts_MunicipalityId_Id\" ON \"Contracts\" (\"MunicipalityId\", \"Id\")");
            if (existing == "wrong-index") await db.Database.ExecuteSqlRawAsync("CREATE INDEX \"AK_Contracts_MunicipalityId_Id\" ON \"Contracts\" (\"Id\")");
            if (existing == "wrong-constraint") await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Contracts\" ADD CONSTRAINT \"AK_Contracts_MunicipalityId_Id\" UNIQUE (\"Id\")");
            if (existing.StartsWith("wrong-"))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(FineMigration));
                Assert.Contains("is incompatible", error.MessageText);
                Assert.DoesNotContain(FineMigration, await db.Database.GetAppliedMigrationsAsync());
                return;
            }
            await db.GetService<IMigrator>().MigrateAsync(FineMigration);
            Assert.Contains(FineMigration, await db.Database.GetAppliedMigrationsAsync());
            await db.GetService<IMigrator>().MigrateAsync(prior); // Rollback must not destroy a pre-existing shared key.
            await db.GetService<IMigrator>().MigrateAsync(FineMigration);
            await db.Database.MigrateAsync(); // Later policy/settlement/NPM-association foreign keys must still apply.
            await db.Database.MigrateAsync(); // Repeated startup must be harmless.
            await using var read = new NpgsqlConnection(connection.ConnectionString); await read.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_constraint WHERE conrelid = '\"Contracts\"'::regclass AND conname = 'AK_Contracts_MunicipalityId_Id' AND contype = 'u'", read);
            Assert.Equal(1L, await count.ExecuteScalarAsync());
            Assert.Equal(0, await db.Collections.CountAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
