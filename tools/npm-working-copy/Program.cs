using System.Security.Cryptography;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

const string migrationId = "20261020050758_AddNpmRentMonthFinancialSource20261020";
Require(args.Length == 1 && args[0] is "verify" or "install" or "seed", "Use verify, install or seed.");
var connection = Environment.GetEnvironmentVariable("STALLTRACK_NPM_WORKCOPY_CONNECTION");
if (args[0] != "verify")
    Require(PreviewMonthlyRentGate.DatabaseRefusals(connection, "stalltrack_workcopy_main", 55491).Count == 0,
        "Only the exact loopback working-copy database on port 55491 is allowed.");
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection
    ?? "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused")
    .AddInterceptors(new MunicipalityStampInterceptor()).Options;
await using var db = new AppDbContext(options);
var registry = db.GetService<IMigrationsAssembly>();
var model = db.GetService<IDesignTimeModel>().Model;
var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(registry.ModelSnapshot!.Model, true);
Require(!db.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(), model.GetRelationalModel()), "Model/snapshot mismatch.");
var migration = registry.CreateMigration(registry.Migrations[migrationId], db.Database.ProviderName!);
Require(migration.UpOperations.Count == 15 && migration.UpOperations.OfType<AddColumnOperation>().Count() == 4
    && migration.UpOperations.OfType<AddColumnOperation>().All(x => x.IsNullable)
    && migration.UpOperations.OfType<AddForeignKeyOperation>().Single().Columns.SequenceEqual(new[] { "MunicipalityId", "NpmRentMonthId" }),
    "Unexpected financial migration operations.");
var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, model).Select(x => x.CommandText)).Replace("\r\n", "\n");
Require(sql == File.ReadAllText("docs/evidence/npm-financial-migration/20261018040000_AddNpmRentMonthFinancialSource.Up.sql.review").Replace("\r\n", "\n"),
    "Financial SQL differs from the independently reviewed candidate.");
if (args[0] == "verify") { Console.WriteLine("Offline verified: 15 reviewed operations; four nullable columns; tenant-composite FK; model/snapshot match. No database connection opened."); return; }
await db.Database.OpenConnectionAsync();
await using (var identity = db.Database.GetDbConnection().CreateCommand())
{
    identity.CommandText = "SELECT current_database(), pg_is_in_recovery()";
    await using var reader = await identity.ExecuteReaderAsync(); await reader.ReadAsync();
    Require(reader.GetString(0) == "stalltrack_workcopy_main" && !reader.GetBoolean(1), "Database identity mismatch.");
}
if (args[0] == "install")
{
    var backup = Environment.GetEnvironmentVariable("STALLTRACK_NPM_BACKUP") ?? "";
    var hash = Environment.GetEnvironmentVariable("STALLTRACK_NPM_BACKUP_SHA256") ?? "";
    Require(File.Exists(backup) && new FileInfo(backup).Length > 0
        && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup))).Equals(hash, StringComparison.OrdinalIgnoreCase), "Verified recoverable backup is required.");
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
    Require(pending.SequenceEqual(new[] { migrationId }) || pending.Length == 0, "Unexpected missing migrations; do not apply unrelated schema.");
    if (pending.Length != 0) await db.Database.GetService<IMigrator>().MigrateAsync(migrationId);
    Console.WriteLine("Verified working-copy migration installed: " + migrationId); return;
}
Require((await db.Database.GetAppliedMigrationsAsync()).Contains(migrationId), "Install the reviewed financial schema before seeding acceptance.");
var output = Environment.GetEnvironmentVariable("STALLTRACK_NPM_TEST_SETUP") ?? throw new InvalidOperationException("Private setup output path required.");
Require(!File.Exists(output), "Setup already exists. Reuse its tenant; never create another on retry.");
var code = "npm-test-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var municipality = Municipality.Create(code, "Synthetic NPM acceptance", "Test environment", MunicipalityStatus.Active,
    tenantCode: code, officeName: "Synthetic Acceptance Office", officeAcronym: "TEST", createdBy: "TEST-NPM-20261020");
db.Add(municipality); await db.SaveChangesAsync();
await using var scoped = new AppDbContext(options, new Tenant(municipality.Id));
await using var transaction = await scoped.Database.BeginTransactionAsync();
var facility = Facility.Create(FacilityCode.NPM, "Synthetic Public Market", "NPM", municipalityId: municipality.Id);
facility.SetMonthBasis(NpmMonthBasis.RentGoal, "TEST-NPM-20261020");
var password = "Test!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
var hashPassword = new IdentityPasswordHasher().Hash(password);
var head = AdminUser.Create("Synthetic NPM Head", "npm-head", "npm-head@example.invalid", hashPassword,
    AdminRole.SuperAdmin, municipality.Id, mustChangePassword: false);
var collector = CollectorUser.Create("Synthetic NPM Collector", "NPM-TEST-01", "npm-collector", null, null, hashPassword, municipality.Id);
collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, municipality.Id);
var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
var service = GovernedService.Create(municipality.Id, CollectorOperationCodes.NpmDaily, "TEST-NPM-20261020");
scoped.AddRange(facility, head, collector, classification, service,
    RevenueClassificationPolicy.Create(classification.Id, today, "Stall rent", RevenueInstrumentType.OfficialReceipt, municipality.Id),
    GovernedServiceSetting.Create(municipality.Id, service.Id, today, GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "TEST-NPM-20261020"));
await scoped.SaveChangesAsync();
var approval = new NpmOpeningMonthPolicy(municipality.Id, facility.Id, today, head.Id, DateTime.UtcNow,
    "TEST-NPM-20261020: project-owner isolated acceptance only; not municipal approval");
// The private file is written before commit; failures leave an explicit setup to reconcile rather than a silent retry.
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { TenantCode = code, HeadUsername = head.Username,
    CollectorUsername = collector.Username, Password = password, WorkingCopyNpmMonthly = new { Enabled = true, Approval = approval } },
    new JsonSerializerOptions { WriteIndented = true }));
await transaction.CommitAsync();
Console.WriteLine($"Synthetic tenant {code}; facility {facility.Id}; private credentials/policy saved. No rates, occupants, Collections or SRCs created. Set rates through the Head API before registration.");

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
{
    public Guid MunicipalityId => id;
    public void Set(Guid municipalityId) => throw new NotSupportedException();
}
