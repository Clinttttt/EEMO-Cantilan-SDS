using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>Offline guard/control-flow evidence, not a PostgreSQL round-trip or constraint proof.</summary>
public class TenantRestoreV2SafetyTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private sealed class Tenant : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => Mid;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Tenant());

    private static TenantRestoreSchema Schema(bool upgraded = false, bool retained = false)
    {
        var tables = new Dictionary<string, IReadOnlyList<RestoreColumn>>
        {
            ["Facilities"] = [new("Id", "uuid", false, false), new("MunicipalityId", "uuid", false, false), new("Name", "text", false, false)]
        };
        if (upgraded) tables["NpmRentMonths"] = [new("Id", "uuid", false, false), new("MunicipalityId", "uuid", false, false)];
        return new("cantilan", tables, retained ? ["RetainedHistoricalEvidence"] : [], 17, true);
    }

    private static TenantRestoreSnapshot Snapshot(TenantRestoreSchema schema) => new(
        TenantRestoreSnapshot.CurrentFormatVersion, "cantilan", Mid, DateTime.UtcNow,
        schema.Tables.Keys.ToDictionary(t => t, _ => "[]"), schema.Fingerprint);

    private static Mock<ITenantRestoreSchemaReader> Reader(TenantRestoreSchema schema)
    {
        var reader = new Mock<ITenantRestoreSchemaReader>(MockBehavior.Strict);
        reader.Setup(r => r.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(schema);
        return reader;
    }

    private sealed class BoundarySpy(AppDbContext context, ITenantRestoreSchemaReader reader)
        : TenantRestoreRepository(context, Mock.Of<ICurrentUserService>(), reader)
    {
        public int DestructivePhaseCalls { get; private set; }
        protected override Task<TenantRestoreResult> RestoreValidatedAsync(TenantRestoreSnapshot snapshot, CancellationToken ct)
        {
            DestructivePhaseCalls++;
            return Task.FromResult(new TenantRestoreResult(0, 0, new Dictionary<string, int>()));
        }
    }

    public static TheoryData<string> InvalidCases => new()
    {
        "v1", "future-version", "tenant", "tenant-code", "fingerprint", "missing-table", "unknown-table",
        "blank", "malformed", "null-json", "object", "scalar-row", "wrong-row-tenant", "missing-column",
        "extra-column", "null-required", "duplicate-column", "duplicate-id", "invalid-id", "null-manifest", "wrong-value-type"
    };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task RejectedSnapshotNeverReachesDeletesAcceptedAuditOrSequenceOperations(string scenario)
    {
        await using var context = Context();
        var schema = Schema();
        var snapshot = Snapshot(schema);
        var id = Guid.NewGuid();
        var row = JsonSerializer.Serialize(new { Id = id, MunicipalityId = Mid, Name = "Synthetic" });
        switch (scenario)
        {
            case "v1": snapshot = snapshot with { FormatVersion = "restore-v1" }; break;
            case "future-version": snapshot = snapshot with { FormatVersion = "restore-v3" }; break;
            case "tenant": snapshot = snapshot with { MunicipalityId = Guid.NewGuid() }; break;
            case "tenant-code": snapshot = snapshot with { TenantCode = "other" }; break;
            case "fingerprint": snapshot = snapshot with { SchemaFingerprint = null }; break;
            case "missing-table": snapshot.Tables.Clear(); break;
            case "unknown-table": snapshot.Tables["AuditLogs"] = "[]"; break;
            case "blank": snapshot.Tables["Facilities"] = " "; break;
            case "malformed": snapshot.Tables["Facilities"] = "["; break;
            case "null-json": snapshot.Tables["Facilities"] = "null"; break;
            case "object": snapshot.Tables["Facilities"] = "{}"; break;
            case "scalar-row": snapshot.Tables["Facilities"] = "[1]"; break;
            case "wrong-row-tenant": snapshot.Tables["Facilities"] = JsonSerializer.Serialize(new[] { new { Id = id, MunicipalityId = Guid.NewGuid(), Name = "Other" } }); break;
            case "missing-column": snapshot.Tables["Facilities"] = JsonSerializer.Serialize(new[] { new { Id = id, MunicipalityId = Mid } }); break;
            case "extra-column": snapshot.Tables["Facilities"] = $"[{row[..^1]},\"Unexpected\":1}}]"; break;
            case "null-required": snapshot.Tables["Facilities"] = JsonSerializer.Serialize(new[] { new { Id = id, MunicipalityId = Mid, Name = (string?)null } }); break;
            case "duplicate-column": snapshot.Tables["Facilities"] = $"[{row[..^1]},\"Name\":\"Duplicate\"}}]"; break;
            case "duplicate-id": snapshot.Tables["Facilities"] = $"[{row},{row}]"; break;
            case "invalid-id": snapshot.Tables["Facilities"] = $"[{row.Replace(id.ToString(), "invalid")}]"; break;
            case "null-manifest": snapshot = snapshot with { Tables = null! }; break;
            case "wrong-value-type": snapshot.Tables["Facilities"] = JsonSerializer.Serialize(new[] { new { Id = id, MunicipalityId = Mid, Name = 123 } }); break;
        }
        var reader = Reader(schema);
        var repository = new BoundarySpy(context, reader.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(snapshot, default));
        Assert.Equal(0, repository.DestructivePhaseCalls);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.AuditLogs.ToListAsync());
        Assert.Equal(17, schema.SrcLastValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteExplicitEmptyManifestReachesValidatedBoundaryOnMatchingSchema(bool upgraded)
    {
        await using var context = Context();
        var schema = Schema(upgraded);
        var repository = new BoundarySpy(context, Reader(schema).Object);
        await repository.RestoreAsync(Snapshot(schema), default);
        Assert.Equal(1, repository.DestructivePhaseCalls);
    }

    [Fact]
    public async Task SnapshotFromBeforeFoundationCannotDestructivelyRestoreIntoUpgradedSchema()
    {
        await using var context = Context();
        var repository = new BoundarySpy(context, Reader(Schema(upgraded: true)).Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(Snapshot(Schema()), default));
        Assert.Equal(0, repository.DestructivePhaseCalls);
    }

    [Fact]
    public async Task NewMonthTableMustBeExplicitlyPresentEvenWhenEmpty()
    {
        await using var context = Context();
        var schema = Schema(upgraded: true);
        var snapshot = Snapshot(schema);
        snapshot.Tables.Remove("NpmRentMonths");
        var repository = new BoundarySpy(context, Reader(schema).Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(snapshot, default));
        Assert.Equal(0, repository.DestructivePhaseCalls);
    }

    [Fact]
    public async Task RetainedTablesBlockBothDestructiveRestoreAndRestoreReadyBackupCreation()
    {
        await using var context = Context();
        var schema = Schema(retained: true);
        var repository = new BoundarySpy(context, Reader(schema).Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(Snapshot(schema), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateSnapshotAsync(default));
        var backups = new TenantBackupRepository(context, repository, Mock.Of<ICurrentUserService>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => backups.CreateAsync("Blocked snapshot", default));
        Assert.Equal(0, repository.DestructivePhaseCalls);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.TenantBackups.ToListAsync());
    }

    [Fact]
    public void UploadedManifestIsFrozenBeforeAsynchronousPreflight()
    {
        var original = Snapshot(Schema());
        var frozen = TenantRestoreValidation.FreezeAndValidateHeader(original, Mid);
        original.Tables.Clear();
        Assert.Single(frozen.Tables);
    }

    [Theory]
    [InlineData(17, true, false)]
    [InlineData(18, true, true)]
    [InlineData(17, false, true)]
    [InlineData(16, false, false)]
    public async Task SrcRecoveryIsRejectedRatherThanAdvancingNontransactionalSequence(long number, bool called, bool reject)
    {
        await using var context = Context();
        var columns = new List<RestoreColumn>
        {
            new("Id", "uuid", false, false), new("MunicipalityId", "uuid", false, false),
            new("ReferenceNumber", "bigint", false, false), new("ReferenceYear", "integer", false, false),
            new("ReferenceCode", "text", false, true)
        };
        var schema = new TenantRestoreSchema("cantilan", new Dictionary<string, IReadOnlyList<RestoreColumn>> { ["Collections"] = columns }, [], 17, called);
        var snapshot = Snapshot(schema);
        snapshot.Tables["Collections"] = JsonSerializer.Serialize(new[] { new { Id = Guid.NewGuid(), MunicipalityId = Mid, ReferenceNumber = number, ReferenceYear = 2026, ReferenceCode = $"SRC-2026-{number:D6}" } });
        var repository = new BoundarySpy(context, Reader(schema).Object);
        if (reject) await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(snapshot, default));
        else await repository.RestoreAsync(snapshot, default);
        Assert.Equal(reject ? 0 : 1, repository.DestructivePhaseCalls);
        Assert.Empty(await context.AuditLogs.ToListAsync());
        Assert.Equal(17, schema.SrcLastValue);
    }

    [Fact]
    public async Task PreFoundationLogicalExportOmitsUnmigratedTableAndDisclosesRetainedObjects()
    {
        await using var context = Context();
        var export = await new TenantExportRepository(context, Reader(Schema(retained: true)).Object).ExportAsync(default);
        Assert.False(export.Tables.ContainsKey("NpmRentMonths"));
        Assert.Contains(export.CompatibilityWarnings!, w => w.Contains("not migrated"));
        Assert.Contains(export.CompatibilityWarnings!, w => w.Contains("Retained historical"));
    }

    [Fact]
    public async Task InvalidHeaderDoesNotEvenReadPhysicalSchema()
    {
        await using var context = Context();
        var reader = new Mock<ITenantRestoreSchemaReader>(MockBehavior.Strict);
        var repository = new BoundarySpy(context, reader.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(Snapshot(Schema()) with { FormatVersion = "restore-v1" }, default));
        reader.VerifyNoOtherCalls();
        Assert.Equal(0, repository.DestructivePhaseCalls);
    }

    [Fact]
    public async Task FailedSchemaInspectionNeverReachesDestructivePhase()
    {
        await using var context = Context();
        var reader = new Mock<ITenantRestoreSchemaReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Incompatible schema"));
        var repository = new BoundarySpy(context, reader.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RestoreAsync(Snapshot(Schema()), default));
        Assert.Equal(0, repository.DestructivePhaseCalls);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("numeric(18,2)", "30.001")]
    [InlineData("numeric(18,2)", "10000000000000000")]
    [InlineData("integer", "1.5")]
    [InlineData("boolean", "\"true\"")]
    [InlineData("date", "\"2026-02-30\"")]
    [InlineData("character varying(3)", "\"long\"")]
    public void LossyOrInvalidColumnValuesAreRejectedBeforeAnyDatabaseWork(string type, string value)
    {
        var schema = Schema();
        var columns = new List<RestoreColumn>(schema.Tables["Facilities"]);
        columns[2] = columns[2] with { StoreType = type };
        schema = schema with { Tables = new Dictionary<string, IReadOnlyList<RestoreColumn>> { ["Facilities"] = columns } };
        var snapshot = Snapshot(schema);
        snapshot.Tables["Facilities"] = $"[{{\"Id\":\"{Guid.NewGuid()}\",\"MunicipalityId\":\"{Mid}\",\"Name\":{value}}}]";
        Assert.Throws<InvalidOperationException>(() => TenantRestoreValidation.Validate(snapshot, Mid, schema));
    }

    [Theory]
    [InlineData("{\"Facilities\":\"[]\",\"Facilities\":\"[]\"}")]
    [InlineData("{\"Facilities\":null}")]
    [InlineData("{\"Facilities\":[]}")]
    [InlineData("[]")]
    public void MalformedUploadedManifestIsRejectedDuringDeserialization(string tables)
    {
        var json = $"{{\"FormatVersion\":\"restore-v2\",\"Tables\":{tables}}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantRestoreSnapshot>(json));
    }

    [Fact]
    public void V2SerializationRetainsExplicitEmptyTableAndSchemaFingerprint()
    {
        var original = Snapshot(Schema());
        var restored = JsonSerializer.Deserialize<TenantRestoreSnapshot>(JsonSerializer.Serialize(original))!;
        Assert.Equal(original.SchemaFingerprint, restored.SchemaFingerprint);
        Assert.Equal("[]", restored.Tables["Facilities"]);
        TenantRestoreValidation.Validate(restored, Mid, Schema());
    }
}
