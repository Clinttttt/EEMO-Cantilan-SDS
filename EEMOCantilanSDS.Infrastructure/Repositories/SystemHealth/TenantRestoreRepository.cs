using System.Data;
using System.Data.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// Per-municipality snapshot + atomic scoped restore. Everything is scoped to the caller's own tenant
/// (<see cref="AppDbContext.CurrentMunicipalityId"/>): the snapshot only reads that tenant's rows, and the
/// restore only ever DELETEs/INSERTs rows with that MunicipalityId. The restore runs in a single
/// transaction (any error → full rollback, zero changes) and re-inserts rows verbatim via Postgres
/// <c>json_populate_recordset</c>, so it is column-for-column faithful with no lossy entity rebuild.
/// The audit log is never restored/overwritten — it is append-only; a single "restore" event is added.
/// </summary>
public class TenantRestoreRepository(AppDbContext context, ICurrentUserService currentUser) : ITenantRestoreRepository
{
    // The vetted set of restorable tenant tables, and the reasoned exclusions, live in TenantDataTables —
    // one statement of what an LGU's backup consists of, held to the model by TenantBackupCoverageTests.
    private static IReadOnlySet<string> RestorableTables => TenantDataTables.Restorable;

    public async Task<TenantRestoreSnapshot> CreateSnapshotAsync(CancellationToken ct)
    {
        var mid = context.CurrentMunicipalityId;
        var order = GetInsertOrder();
        var tables = new Dictionary<string, string>();

        var muni = await context.Municipalities.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mid, ct);

        var connection = context.Database.GetDbConnection();
        var openedHere = false;
        try
        {
            if (connection.State != ConnectionState.Open) { await connection.OpenAsync(ct); openedHere = true; }

            if (mid != Guid.Empty)
            {
                foreach (var (schema, table) in order)
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText =
                        $"SELECT COALESCE(json_agg(row_to_json(t)), '[]'::json)::text " +
                        $"FROM \"{schema}\".\"{table}\" t WHERE t.\"MunicipalityId\" = @mid;";
                    AddParam(cmd, "mid", mid);
                    var json = await cmd.ExecuteScalarAsync(ct);
                    tables[table] = json as string ?? "[]";
                }
            }
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                try { await connection.CloseAsync(); } catch { /* best-effort */ }
            }
        }

        return new TenantRestoreSnapshot(
            TenantRestoreSnapshot.CurrentFormatVersion,
            muni?.TenantCode ?? "tenant",
            mid,
            DateTime.UtcNow,
            tables);
    }

    public async Task<TenantRestoreResult> RestoreAsync(TenantRestoreSnapshot snapshot, CancellationToken ct)
    {
        var mid = context.CurrentMunicipalityId;

        // Hard scoping guards — a restore can only ever target the caller's OWN tenant, and only the
        // faithful format. Any mismatch aborts BEFORE touching a single row.
        if (mid == Guid.Empty)
            throw new InvalidOperationException("No municipality is resolved for this request.");
        if (snapshot is null)
            throw new InvalidOperationException("The restore snapshot is missing.");
        if (!string.Equals(snapshot.FormatVersion, TenantRestoreSnapshot.CurrentFormatVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("This backup file is not a restore-ready snapshot for this version.");
        if (snapshot.MunicipalityId != mid)
            throw new InvalidOperationException("This backup belongs to a different municipality and cannot be restored here.");

        var order = GetInsertOrder();                 // parents → children
        var perTable = new Dictionary<string, int>();

        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var connection = context.Database.GetDbConnection();
        var dbTx = tx.GetDbTransaction();

        // 1) Clear this tenant's current rows, children → parents (reverse FK order).
        foreach (var (schema, table) in Enumerable.Reverse(order))
        {
            await using var del = connection.CreateCommand();
            del.Transaction = dbTx;
            del.CommandText = $"DELETE FROM \"{schema}\".\"{table}\" WHERE \"MunicipalityId\" = @mid;";
            AddParam(del, "mid", mid);
            await del.ExecuteNonQueryAsync(ct);
        }

        // 2) Re-insert from the snapshot, parents → children, verbatim (json_populate_recordset). Generated columns (the
        // stored SRC code) are never inserted: the database recomputes them from the restored year and number, so a restore
        // reproduces each Collection's original SRC and never allocates a new one.
        foreach (var (schema, table) in order)
        {
            if (!snapshot.Tables.TryGetValue(table, out var json) || string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                perTable[table] = 0;
                continue;
            }

            var columns = await InsertableColumnsAsync(connection, dbTx, schema, table, ct);
            await using var ins = connection.CreateCommand();
            ins.Transaction = dbTx;
            ins.CommandText =
                $"INSERT INTO \"{schema}\".\"{table}\" ({columns}) " +
                $"SELECT {columns} FROM json_populate_recordset(NULL::\"{schema}\".\"{table}\", @json::json);";
            AddParam(ins, "json", json);
            try
            {
                perTable[table] = await ins.ExecuteNonQueryAsync(ct);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
                && string.Equals(table, "Collections", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "This backup contains collection reference codes (SRC) that already exist in this database. " +
                    "Nothing was restored; reference codes are never regenerated or renumbered.", ex);
            }
        }

        // The SRC sequence must never hand out a number a restored Collection already carries.
        await using (var seq = connection.CreateCommand())
        {
            seq.Transaction = dbTx;
            seq.CommandText =
                "SELECT setval('\"CollectionReferenceNumberSeq\"', GREATEST(" +
                "(SELECT COALESCE(MAX(\"ReferenceNumber\"), 1) FROM \"Collections\"), " +
                "(SELECT last_value FROM \"CollectionReferenceNumberSeq\")), true);";
            await seq.ExecuteScalarAsync(ct);
        }

        // 3) Append (never overwrite) an audit event for the restore itself, with a structured per-table
        // breakdown in NewValues so the restore history can show exactly what was restored.
        var rows = perTable.Values.Sum();
        var tablesTouched = perTable.Count(kv => kv.Value > 0);
        var newValues = System.Text.Json.JsonSerializer.Serialize(new
        {
            rows,
            tables = tablesTouched,
            snapshotUtc = snapshot.GeneratedAtUtc,
            perTable = perTable.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value),
        });
        context.AuditLogs.Add(AuditLog.Create(
            actorId: currentUser.UserId?.ToString() ?? currentUser.Username ?? "system",
            actorName: currentUser.Username ?? "system",
            actorRole: currentUser.Role ?? "SuperAdmin",
            action: "TenantRestore",
            entityType: "Municipality",
            entityId: mid,
            newValues: newValues,
            notes: $"Restored {rows} row(s) across {tablesTouched} table(s) from a snapshot taken {snapshot.GeneratedAtUtc:u}."));
        await context.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return new TenantRestoreResult(tablesTouched, rows, perTable);
    }

    /// <summary>The table's columns that may be inserted: everything except database-generated columns.</summary>
    private static async Task<string> InsertableColumnsAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction tx, string schema, string table, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = @schema AND table_name = @table AND is_generated = 'NEVER' ORDER BY ordinal_position;";
        AddParam(cmd, "schema", schema);
        AddParam(cmd, "table", table);
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add("\"" + reader.GetString(0).Replace("\"", "\"\"") + "\"");
        return string.Join(", ", names);
    }

    // Restorable tables in dependency (insert) order: a table appears AFTER every restorable table it
    // references by foreign key. Derived from the live EF model so it never drifts from the schema.
    private List<(string Schema, string Table)> GetInsertOrder()
    {
        var types = context.Model.GetEntityTypes()
            .Where(t => t.BaseType is null)
            .Where(t => t.GetTableName() is { } name && RestorableTables.Contains(name))
            .Distinct()
            .ToList();

        var inSet = new HashSet<IEntityType>(types);
        var visited = new HashSet<IEntityType>();
        var order = new List<IEntityType>();

        void Visit(IEntityType t)
        {
            if (!visited.Add(t)) return;
            foreach (var fk in t.GetForeignKeys())
            {
                var principal = fk.PrincipalEntityType;
                while (principal.BaseType is not null) principal = principal.BaseType;   // resolve to root (TPH)
                if (!ReferenceEquals(principal, t) && inSet.Contains(principal))
                    Visit(principal);
            }
            order.Add(t);   // post-order → dependencies first
        }

        foreach (var t in types) Visit(t);

        return order
            .Select(t => (Schema: t.GetSchema() ?? "public", Table: t.GetTableName()!))
            .ToList();
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
