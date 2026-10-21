using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

const string migrationId = "20261017040000_AddNpmRentMonthFoundation";
const string migrationName = "AddNpmRentMonthFoundation";
const string migrationNamespace = "EEMOCantilanSDS.Infrastructure.Migrations";
var root = Path.GetFullPath(args.FirstOrDefault(a => a.StartsWith("--root="))?[7..] ?? Directory.GetCurrentDirectory());
var directory = Path.Combine(root, "docs/evidence/task2d3");
Directory.CreateDirectory(directory);
using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused")
    .AddInterceptors(new ForbidDatabaseConnection()).Options);
var registry = db.GetService<IMigrationsAssembly>();
var initializer = db.GetService<IModelRuntimeInitializer>();
var original = initializer.Initialize(registry.ModelSnapshot!.Model, designTime: true);
var current = db.GetService<IDesignTimeModel>().Model;
var differ = db.GetService<IMigrationsModelDiffer>();
var operations = differ.GetDifferences(original.GetRelationalModel(), current.GetRelationalModel());
Require(operations.Count == 7 && operations.OfType<AddUniqueConstraintOperation>().Count() == 2
    && operations.OfType<CreateTableOperation>().Count() == 1 && operations.OfType<CreateIndexOperation>().Count() == 4
    && !operations.Any(o => o.IsDestructiveChange), "The approved seven-operation difference changed.");
Require(registry.Migrations.Count == 130 && !registry.Migrations.ContainsKey(migrationId), "Production registry changed or candidate is already active.");
Require(string.CompareOrdinal(registry.Migrations.Keys.Max(), migrationId) < 0, "Candidate ID must follow the current compiled history.");
// The latest migration is hand-authored without BuildTargetModel; use the accepted full snapshot.
var latestHasTargetModel = registry.CreateMigration(registry.Migrations[registry.Migrations.Keys.Max()!], db.Database.ProviderName!)
    .TargetModel.GetEntityTypes().Any();

string Sql(IReadOnlyList<MigrationOperation> ops, IModel model) => string.Join("\n",
    db.GetService<IMigrationsSqlGenerator>().Generate(ops, model).Select(c => c.CommandText)).Replace("\r\n", "\n");
var rehearsedSql = File.ReadAllText(Path.Combine(root, "docs/evidence/task2d/NpmRentMonthFoundation.Up.sql.review"))
    .Replace("\r\n", "\n").Split('\n', 2)[1];
Require(Sql(operations, current) == rehearsedSql, "Current generated SQL differs from the rehearsed DDL.");

if (args.Contains("generate"))
{
    var services = new ServiceCollection();
    services.AddEntityFrameworkDesignTimeServices();
    services.AddDbContextDesignTimeServices(db);
    new Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal.NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
    using var provider = services.BuildServiceProvider();
    var generator = provider.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#");
    var migration = generator.GenerateMigration(migrationNamespace, migrationName, operations, [])
        .Replace("\r\n", "\n")
        .Replace("protected override void Down(MigrationBuilder migrationBuilder)\n        {\n\n        }",
            "protected override void Down(MigrationBuilder migrationBuilder)\n        {\n            throw new System.NotSupportedException(\"Forward-only NPM evidence schema. An approved forward recovery plan is required.\");\n        }");
    Require(migration.Contains("throw new System.NotSupportedException"), "Forward-only Down guard generation failed.");
    Create(migrationId + ".cs.review", migration);
    Create(migrationId + ".Designer.cs.review", generator.GenerateMetadata(migrationNamespace, typeof(AppDbContext), migrationName, migrationId, current));
    Create("AppDbContextModelSnapshot.cs.review", generator.GenerateSnapshot(migrationNamespace, typeof(AppDbContext), "AppDbContextModelSnapshot", current));
    Create(migrationId + ".Up.sql.review", Sql(operations, current));
    Console.WriteLine("Generated finalized candidates only. Rebuild this offline tool, then run verify.");
    return;
}
Require(args.Contains("verify"), "Use generate or verify.");
var candidateType = Assembly.GetExecutingAssembly().GetType(migrationNamespace + "." + migrationName)
    ?? throw new InvalidOperationException("Candidate migration is not compiled into the offline tool.");
var candidate = (Migration)Activator.CreateInstance(candidateType)!;
candidate.ActiveProvider = db.Database.ProviderName!;
var target = initializer.Initialize(candidate.TargetModel, designTime: true);
var snapshotType = Assembly.GetExecutingAssembly().GetType(migrationNamespace + ".AppDbContextModelSnapshot")!;
var candidateSnapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
var snapshotModel = initializer.Initialize(candidateSnapshot.Model, designTime: true);
Require(candidateType.GetCustomAttribute<MigrationAttribute>()?.Id == migrationId, "Migration ID metadata mismatch.");
Require(candidateType.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(AppDbContext), "Migration context metadata mismatch.");
Require(!differ.HasDifferences(target.GetRelationalModel(), current.GetRelationalModel()), "Designer target model differs from current model.");
Require(!differ.HasDifferences(snapshotModel.GetRelationalModel(), target.GetRelationalModel()), "Candidate snapshot differs from designer target model.");
Require(Sql(candidate.UpOperations, target) == rehearsedSql && candidate.UpOperations.Count == 7, "Compiled migration differs from rehearsed SQL.");
Require(db.GetService<IMigrationsSqlGenerator>().Generate(candidate.UpOperations, target).All(c => !c.TransactionSuppressed),
    "Every foundation command must remain eligible for EF's migration transaction.");
var moneyColumns = candidate.UpOperations.OfType<CreateTableOperation>().Single().Columns
    .Where(c => c.Name is "AssessedAmount" or "CreditedAmount").ToArray();
Require(moneyColumns.Length == 2 && moneyColumns.All(c => c.ClrType == typeof(decimal)
    && c.ColumnType == "numeric(18,2)" && !c.IsNullable), "Compiled money columns must retain required numeric(18,2).");
var snapshotOperations = differ.GetDifferences(original.GetRelationalModel(), snapshotModel.GetRelationalModel());
Require(snapshotOperations.Count == 7 && snapshotOperations.OfType<AddUniqueConstraintOperation>().Count() == 2
    && snapshotOperations.OfType<CreateTableOperation>().Count() == 1 && snapshotOperations.OfType<CreateIndexOperation>().Count() == 4
    && !snapshotOperations.Any(o => o.IsDestructiveChange), "Snapshot delta must remain exactly seven additive operations.");
var snapshotSql = Sql(snapshotOperations, snapshotModel);
Console.WriteLine("Snapshot delta operation order: " + string.Join(", ", snapshotOperations.Select(o => o switch
{
    AddUniqueConstraintOperation key => key.Name,
    CreateTableOperation table => table.Name + " columns: " + string.Join(",", table.Columns.Select(c => c.Name)),
    CreateIndexOperation index => index.Name,
    _ => o.GetType().Name
})));
var downBlocked = false;
try { _ = candidate.DownOperations; } catch (NotSupportedException) { downBlocked = true; }
Require(downBlocked, "Down must refuse destructive reversal before any database command.");
var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/evidence/2026-10-17_ef_migration_lineage_metadata.json")));
var applied = manifest.RootElement.GetProperty("appliedMigrations").EnumerateArray()
    .Select(x => x.GetProperty("MigrationId").GetString()!).ToHashSet(StringComparer.Ordinal);
Require(applied.Count == 143 && registry.Migrations.Keys.All(applied.Contains), "Accepted retained history is incompatible with current compiled history.");
var proposedRegistry = registry.Migrations.Keys.Append(migrationId).ToArray();
var pending = proposedRegistry.Where(id => !applied.Contains(id)).ToArray();
Require(pending.SequenceEqual(new[] { migrationId }), "More than the approved foundation is pending.");
var historyInsert = db.GetService<IHistoryRepository>().GetInsertScript(new HistoryRow(migrationId, ProductInfo.GetVersion()));
Require(historyInsert.StartsWith("INSERT INTO ", StringComparison.Ordinal) && !historyInsert.Contains("DELETE", StringComparison.Ordinal), "History bookkeeping must be a normal additive EF insert.");
var evidence = new
{
    task = "2D.3", checkedAtUtc = DateTime.UtcNow, migrationId, latestCompiled = registry.Migrations.Keys.Max(),
    activeCompiledCount = registry.Migrations.Count, acceptedAppliedCount = applied.Count,
    baselineSource = registry.ModelSnapshot!.GetType().FullName, latestCompiledMigrationHasTargetModel = latestHasTargetModel,
    retainedExtraIds = applied.Except(registry.Migrations.Keys).Order().ToArray(),
    proposedCompiledCount = proposedRegistry.Length, pending, historyInsert,
    operations = candidate.UpOperations.Select(o => o.GetType().Name).ToArray(),
    rehearsedSqlMatches = true, designerMatchesCurrentModel = true, snapshotMatchesDesigner = true,
    snapshotDeltaOperations = snapshotOperations.Select(o => o.GetType().Name).ToArray(),
    snapshotDeltaSqlByteIdentical = snapshotSql == rehearsedSql, downBlocked,
    moneyColumns = moneyColumns.Select(c => new { c.Name, c.ColumnType, c.IsNullable }).ToArray(),
    transactionSuppressedCommands = 0, databaseConnectionsOpened = 0, activeSnapshotUnchanged = true,
    files = Directory.GetFiles(directory, "*.review").Order().Select(file => new { file = Path.GetFileName(file), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant() }).ToArray()
};
Create("verification-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(evidence));

void Create(string name, string contents)
{
    using var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
    writer.Write(contents.Replace("\r\n", "\n"));
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
sealed class ForbidDatabaseConnection : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        => throw new InvalidOperationException("Offline readiness tooling must never open a database connection.");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Offline readiness tooling must never open a database connection.");
}
