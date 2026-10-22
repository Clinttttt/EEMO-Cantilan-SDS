using System.Text.Json;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

// Deliberately no application host, DI registration, seeding, retry policy or financial service.
const string id = "20261017040000_AddNpmRentMonthFoundation";
var apply = args.SequenceEqual(new[] { "apply" });
Require(apply || args.SequenceEqual(new[] { "verify" }), "Use verify or apply.");
var connection = new NpgsqlConnectionStringBuilder(apply
    ? Environment.GetEnvironmentVariable("STALLTRACK_INSTALL_CONNECTION")
        ?? throw new InvalidOperationException("Missing controlled connection.")
    : "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused");
connection.Pooling = false;
connection.CommandTimeout = 30;
if (apply)
{
    Require(Environment.GetEnvironmentVariable("STALLTRACK_FOUNDATION_APPROVAL") == "owner-approved-local-foundation-only",
        "Explicit local foundation acknowledgement required.");
    Require(connection.Host is "localhost" or "127.0.0.1" or "::1"
        && connection.Port == 5432 && connection.Database == "EEMOCantilanSDS.DB", "Wrong database target.");
}
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
var registry = db.GetService<IMigrationsAssembly>();
Require(registry.Migrations.Count == 131 && registry.Migrations.Keys.Max() == id, "Unexpected compiled migration registry.");
var initializer = db.GetService<IModelRuntimeInitializer>();
var target = initializer.Initialize(registry.CreateMigration(registry.Migrations[id], db.Database.ProviderName!).TargetModel, true);
var current = db.GetService<IDesignTimeModel>().Model;
var snapshot = initializer.Initialize(registry.ModelSnapshot!.Model, true);
var differ = db.GetService<IMigrationsModelDiffer>();
Require(!differ.HasDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel())
    && !differ.HasDifferences(target.GetRelationalModel(), current.GetRelationalModel()), "Snapshot/model mismatch.");
var migration = registry.CreateMigration(registry.Migrations[id], db.Database.ProviderName!);
Require(migration.UpOperations.Count == 7
    && migration.UpOperations.OfType<AddUniqueConstraintOperation>().Count() == 2
    && migration.UpOperations.OfType<CreateTableOperation>().Count() == 1
    && migration.UpOperations.OfType<CreateIndexOperation>().Count() == 4
    && !migration.UpOperations.Any(o => o.IsDestructiveChange), "Unexpected migration operations.");
var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, target);
Require(commands.All(c => !c.TransactionSuppressed), "Nontransactional migration command.");
var generated = string.Join("\n", commands.Select(c => c.CommandText)).Replace("\r\n", "\n");
var reviewed = File.ReadAllText("docs/evidence/task2d3/" + id + ".Up.sql.review").Replace("\r\n", "\n");
Require(generated == reviewed, "Reviewed SQL mismatch.");
Require(!db.Database.CreateExecutionStrategy().RetriesOnFailure, "Automatic retries forbidden.");
if (!apply)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "offline_verified", compiled = 131, operations = 7, snapshotMatches = true }));
    return;
}
await db.Database.OpenConnectionAsync();
var cn = (NpgsqlConnection)db.Database.GetDbConnection();
async Task<object?> Scalar(string sql)
{
    await using var cmd = new NpgsqlCommand(sql, cn);
    return await cmd.ExecuteScalarAsync();
}
Require((string?)await Scalar("SELECT current_database()") == "EEMOCantilanSDS.DB", "Database identity changed.");
Require((string?)await Scalar("SELECT host(inet_server_addr())") is "127.0.0.1" or "::1", "Nonlocal server.");
Require((int?)await Scalar("SELECT inet_server_port()") == 5432, "Unexpected server port.");
Require((long?)await Scalar("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()") == 0,
    "Another database session is present.");
await Scalar("SET search_path TO public,pg_catalog; SET lock_timeout TO '1500ms'; SET statement_timeout TO '30s'");
Require((string?)await Scalar("SELECT current_schema()") == "public", "Unexpected schema.");
Require((bool?)await Scalar("SELECT pg_try_advisory_lock(8472113509001)") == true, "Maintenance lock unavailable.");
try
{
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
    Require(pending.SequenceEqual(new[] { id }), "Only the approved foundation may be pending.");
    Require((await db.Database.GetAppliedMigrationsAsync()).Count() == 143, "Applied history changed.");
    // The coordinator fsyncs intent before invoking this process. A thrown MigrateAsync may have committed:
    // never infer rollback or retry here. Fresh independent read-only postflight decides the outcome.
    Console.WriteLine("migration_invocation_started");
    await db.GetService<IMigrator>().MigrateAsync(id);
    Console.WriteLine(JsonSerializer.Serialize(new { status = "ef_migration_acknowledged", id }));
}
finally
{
    await Scalar("SELECT pg_advisory_unlock(8472113509001)");
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
