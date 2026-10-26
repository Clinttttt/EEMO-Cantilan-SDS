using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The additive month-aware term migration, applied on top of the previous schema with existing space data: every existing row is
/// byte-for-byte unchanged, the new columns start empty, and only then can a months-only term be recorded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaceContractMonthsMigrationTests(PostgresFixture database)
{
    private const string Migration = "20261026085122_SpaceContractTermMonths20261026";

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task Migration_AppliesOverExistingSpaceData_LeavingItUnchanged_AndOnlyThenAllowsAMonthsOnlyTerm()
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "");
        await using var admin = database.CreateRawConnection();
        var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Pooling = false };
        await admin.OpenAsync();
        var name = "space_months_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.Database = name;
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).AddInterceptors(new MunicipalityStampInterceptor()).Options;
            var municipality = Municipality.Create($"MM-{Guid.NewGuid():N}"[..20], "Months", "Province", MunicipalityStatus.Active, tenantCode: $"mm-{Guid.NewGuid():N}"[..16], isDefault: true);
            Guid accountId;
            await using (var seed = new AppDbContext(options, new FixedTenant(municipality.Id)))
            {
                await seed.Database.MigrateAsync();
                seed.Municipalities.Add(municipality);
                var registration = SpaceRegistration.Create(municipality.Id, ObligationKind.KanmanggaySpaceRental, "1", "test");
                var account = ObligationAccount.Create(municipality.Id, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "1", null, null, new DateOnly(2022, 10, 1), "test", actualOccupant: "Holder");
                account.LinkRegistration(registration.Id);
                account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "REF");
                account.SetLeaseDetails("Lessee", new DateOnly(2022, 10, 25), 3, 9m, "Row A", 800m);
                accountId = account.Id;
                seed.SpaceRegistrations.Add(registration);
                seed.ObligationAccounts.Add(account);
                seed.ObligationRates.Add(ObligationRate.Create(municipality.Id, account.Id, account.ActiveFrom, 1000m, "test"));
                var evt = SpaceLifecycleEvent.Record(municipality.Id, registration.Id, SpaceLifecycleAction.Renew, account.Id, null, new DateOnly(2026, 1, 1), Guid.NewGuid(),
                    new string('a', 64), new string('b', 64), "Renewal", "Res", Guid.NewGuid(), "test", DateTime.UtcNow);
                seed.SpaceLifecycleEvents.Add(evt);
                seed.SpaceContractTerms.Add(SpaceContractTerm.Create(municipality.Id, account.Id, registration.Id, null, new DateOnly(2026, 1, 1), 2, "Lessee", "REF2", "Res", evt.Id, Guid.NewGuid(), "test", DateTime.UtcNow));
                await seed.SaveChangesAsync();
            }

            await using var db = new AppDbContext(options, new FixedTenant(municipality.Id));
            var migrator = db.GetService<IMigrator>();
            var prior = db.Database.GetMigrations().Where(x => string.CompareOrdinal(x, Migration) < 0).Last();
            await migrator.MigrateAsync(prior);                                      // back to the schema this migration sits on top of
            Assert.DoesNotContain(Migration, await db.Database.GetAppliedMigrationsAsync());
            async Task<(string Accounts, string Terms)> SnapshotAsync()
            {
                await using var read = new NpgsqlConnection(connection.ConnectionString);
                await read.OpenAsync();
                // Existing columns only: the new ones are compared separately below.
                await using var a = new NpgsqlCommand("SELECT coalesce(string_agg((to_jsonb(t) - 'ContractMonths')::text, '|' ORDER BY \"Id\"), '') FROM \"ObligationAccounts\" t", read);
                await using var s = new NpgsqlCommand("SELECT coalesce(string_agg((to_jsonb(t) - 'Months')::text, '|' ORDER BY \"Id\"), '') FROM \"SpaceContractTerms\" t", read);
                return ((string)(await a.ExecuteScalarAsync())!, (string)(await s.ExecuteScalarAsync())!);
            }
            var before = await SnapshotAsync();
            Assert.NotEmpty(before.Accounts);
            Assert.NotEmpty(before.Terms);

            await migrator.MigrateAsync(Migration);
            Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(before, await SnapshotAsync());                              // nothing existing changed
            await using (var read = new NpgsqlConnection(connection.ConnectionString))
            {
                await read.OpenAsync();
                await using var nulls = new NpgsqlCommand("SELECT (SELECT count(*) FROM \"ObligationAccounts\" WHERE \"ContractMonths\" IS NOT NULL) + (SELECT count(*) FROM \"SpaceContractTerms\" WHERE \"Months\" IS NOT NULL)", read);
                Assert.Equal(0L, await nulls.ExecuteScalarAsync());
            }

            // The relaxed check: a months-only term is now valid; a zero-year term without months, or months over 11, still is not.
            var registrationId = (await db.SpaceRegistrations.AsNoTracking().SingleAsync()).Id;
            var lifecycle = SpaceLifecycleEvent.Record(municipality.Id, registrationId, SpaceLifecycleAction.Renew, accountId, null, new DateOnly(2028, 1, 1), Guid.NewGuid(),
                new string('c', 64), new string('d', 64), "Renewal", "Res", Guid.NewGuid(), "test", DateTime.UtcNow);
            db.SpaceLifecycleEvents.Add(lifecycle);
            db.SpaceContractTerms.Add(SpaceContractTerm.Create(municipality.Id, accountId, registrationId, null, new DateOnly(2028, 1, 1), 0, "Lessee", "REF3", "Res", lifecycle.Id, Guid.NewGuid(), "test", DateTime.UtcNow, 6));
            await db.SaveChangesAsync();
            foreach (var bad in new[] { "0, NULL", "0, 12", "1, 0" })
            {
                await using var insert = new NpgsqlConnection(connection.ConnectionString);
                await insert.OpenAsync();
                await using var cmd = new NpgsqlCommand($@"INSERT INTO ""SpaceContractTerms"" (""Id"", ""MunicipalityId"", ""ObligationAccountId"", ""SpaceRegistrationId"", ""Commencement"", ""Years"", ""Months"", ""ApprovalEvidence"", ""LifecycleEventId"", ""ActorId"", ""ActorName"", ""RecordedAtUtc"")
                    VALUES (gen_random_uuid(), '{municipality.Id}', '{accountId}', '{registrationId}', '2030-01-01', {bad.Split(',')[0]}, {bad.Split(',')[1]}, 'x', '{lifecycle.Id}', gen_random_uuid(), 'x', now())", insert);
                var error = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
                Assert.Equal("23514", error.SqlState);
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
