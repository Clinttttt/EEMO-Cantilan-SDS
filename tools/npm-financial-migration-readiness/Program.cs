using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

const string id = "20261018040000_AddNpmRentMonthFinancialSource";
const string name = "AddNpmRentMonthFinancialSource";
const string ns = "EEMOCantilanSDS.Infrastructure.Migrations";
var root = Path.GetFullPath(args.SingleOrDefault(a => a.StartsWith("--root="))?[7..] ?? Directory.GetCurrentDirectory());
var directory = Path.Combine(root, "docs/evidence/npm-financial-migration");
Require(args.Count(a => a is "generate" or "verify") == 1, "Use generate or verify; no apply mode exists.");
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused")
    .AddInterceptors(new ForbidConnection()).Options;
using var active = new AppDbContext(options);
using var preview = new Preview(options);
var registry = active.GetService<IMigrationsAssembly>();
var initializer = active.GetService<IModelRuntimeInitializer>();
var baseline = initializer.Initialize(registry.ModelSnapshot!.Model, designTime: true);
var current = active.GetService<IDesignTimeModel>().Model;
var target = preview.GetService<IDesignTimeModel>().Model;
var differ = active.GetService<IMigrationsModelDiffer>();
Require(registry.Migrations.Count == 131 && registry.Migrations.Keys.Max() == "20261017040000_AddNpmRentMonthFoundation",
    "Active migration lineage changed; review instead of guessing a new baseline.");
Require(!differ.HasDifferences(baseline.GetRelationalModel(), current.GetRelationalModel()), "Active snapshot/model differ.");
var operations = differ.GetDifferences(current.GetRelationalModel(), target.GetRelationalModel());
Require(operations.OfType<AddColumnOperation>().Count() == 4, "Expected four nullable financial columns.");
Require(operations.All(o => o is AddColumnOperation or CreateIndexOperation or AddForeignKeyOperation
    or AddCheckConstraintOperation or DropCheckConstraintOperation), "Unexpected financial operation.");
Require(operations.OfType<AddColumnOperation>().All(c => c.IsNullable), "Existing records must remain nullable-compatible.");
Require(operations.OfType<AddForeignKeyOperation>().Single().Columns.SequenceEqual(new[] { "MunicipalityId", "NpmRentMonthId" }), "Typed FK changed.");
foreach (var property in new[] { "AssessedAmount", "CreditedAmount" })
{
    var money = target.FindEntityType(typeof(NpmRentMonth))!.FindProperty(property)!;
    Require(money.GetPrecision() == 18 && money.GetScale() == 2, "Monthly money precision changed.");
}
string Sql(IReadOnlyList<MigrationOperation> ops, IModel model)
{
    var commands = active.GetService<IMigrationsSqlGenerator>().Generate(ops, model);
    Require(commands.All(c => !c.TransactionSuppressed), "Nontransactional command requires separate approval.");
    return string.Join("\n", commands.Select(c => c.CommandText)).Replace("\r\n", "\n");
}
var sql = Sql(operations, target);
if (args.Contains("generate"))
{
    var services = new ServiceCollection();
    services.AddEntityFrameworkDesignTimeServices(); services.AddDbContextDesignTimeServices(active);
    new Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal.NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
    using var provider = services.BuildServiceProvider();
    var generator = provider.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#");
    var migration = generator.GenerateMigration(ns, name, operations, []).Replace("\r\n", "\n")
        .Replace("protected override void Down(MigrationBuilder migrationBuilder)\n        {\n\n        }",
            "protected override void Down(MigrationBuilder migrationBuilder)\n        {\n            throw new System.NotSupportedException(\"Financial evidence is forward-only. Approved recovery is required.\");\n        }");
    Require(migration.Contains("throw new System.NotSupportedException"), "Forward-only guard missing.");
    WriteNew(id + ".cs.review", migration);
    WriteNew(id + ".Designer.cs.review", generator.GenerateMetadata(ns, typeof(AppDbContext), name, id, target));
    WriteNew("AppDbContextModelSnapshot.cs.review", generator.GenerateSnapshot(ns, typeof(AppDbContext), "AppDbContextModelSnapshot", target));
    WriteNew(id + ".Up.sql.review", sql);
    WriteNew("manifest.json", JsonSerializer.Serialize(new {
        candidateId = id, activeFoundation = registry.Migrations.Keys.Max(), activeCompiledCount = registry.Migrations.Count,
        operations = operations.Select(o => new { type = o.GetType().Name, table = o switch {
            AddColumnOperation c => c.Table, CreateIndexOperation i => i.Table, AddForeignKeyOperation f => f.Table,
            AddCheckConstraintOperation c => c.Table, DropCheckConstraintOperation c => c.Table, _ => "" },
            name = o switch { AddColumnOperation c => c.Name, CreateIndexOperation i => i.Name, AddForeignKeyOperation f => f.Name,
                AddCheckConstraintOperation c => c.Name, DropCheckConstraintOperation c => c.Name, _ => "" } }),
        files = Directory.GetFiles(directory, "*.review").Order().Select(f => new { file = Path.GetFileName(f), sha256 = Hash(f) }),
        databaseConnectionsOpened = 0, appliesMigration = false
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Review-only artifacts generated. Rebuild, then verify independently; nothing promoted or applied.");
    return;
}
var assembly = Assembly.GetExecutingAssembly();
var type = assembly.GetType(ns + "." + name) ?? throw new InvalidOperationException("Generate artifacts and rebuild first.");
var compiled = (Migration)Activator.CreateInstance(type)!; compiled.ActiveProvider = active.Database.ProviderName!;
var compiledTarget = initializer.Initialize(compiled.TargetModel, designTime: true);
var snapshot = (ModelSnapshot)Activator.CreateInstance(assembly.GetType(ns + ".AppDbContextModelSnapshot")!, true)!;
var compiledSnapshot = initializer.Initialize(snapshot.Model, designTime: true);
Require(type.GetCustomAttribute<MigrationAttribute>()?.Id == id && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(AppDbContext), "Candidate metadata mismatch.");
Require(!differ.HasDifferences(target.GetRelationalModel(), compiledTarget.GetRelationalModel()), "Designer/model mismatch.");
Require(!differ.HasDifferences(target.GetRelationalModel(), compiledSnapshot.GetRelationalModel()), "Snapshot/model mismatch.");
Require(Sql(compiled.UpOperations, compiledTarget) == sql && File.ReadAllText(Path.Combine(directory, id + ".Up.sql.review")) == sql, "Compiled/review SQL mismatch.");
var downBlocked = false;
try { _ = compiled.DownOperations; } catch (NotSupportedException) { downBlocked = true; }
Require(downBlocked, "Destructive Down must be unavailable.");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
    Require(Hash(Path.Combine(directory, file.GetProperty("file").GetString()!)) == file.GetProperty("sha256").GetString(), "Artifact hash mismatch.");
Require(!registry.Migrations.ContainsKey(id), "Candidate leaked into active assembly.");
Console.WriteLine(JsonSerializer.Serialize(new { status = "offline_verified", id, operations = operations.Count,
    compiledActive = registry.Migrations.Count, snapshotMatches = true, designerMatches = true, sqlMatches = true,
    hashesMatch = true, downBlocked, databaseConnectionsOpened = 0, runtimeActivated = false }));

void WriteNew(string file, string content)
{
    Directory.CreateDirectory(directory);
    using var stream = new FileStream(Path.Combine(directory, file), FileMode.CreateNew, FileAccess.Write);
    using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(content.Replace("\r\n", "\n"));
}
static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
sealed class Preview(DbContextOptions<AppDbContext> options) : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model) { base.OnModelCreating(model); NpmRentMonthFinancialSchema.Configure(model); }
}
sealed class ForbidConnection : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result)
        => throw new InvalidOperationException("Offline package tooling forbids database connections.");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data, InterceptionResult result, CancellationToken ct = default)
        => throw new InvalidOperationException("Offline package tooling forbids database connections.");
}
