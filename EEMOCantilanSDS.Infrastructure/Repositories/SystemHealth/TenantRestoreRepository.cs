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
public class TenantRestoreRepository(AppDbContext context, ICurrentUserService currentUser,
    ITenantRestoreSchemaReader? schemaReader = null) : ITenantRestoreRepository
{
    private readonly ITenantRestoreSchemaReader schemaReader = schemaReader ?? new TenantRestoreSchemaReader(context);
    // The vetted set of restorable tenant tables, and the reasoned exclusions, live in TenantDataTables —
    // one statement of what an LGU's backup consists of, held to the model by TenantBackupCoverageTests.
    private static IReadOnlySet<string> RestorableTables => TenantDataTables.Restorable;

    public Task<TenantRestoreSnapshot> CreateSnapshotAsync(CancellationToken ct) => CaptureAsync(storedBackup: false, ct);

    public Task<TenantRestoreSnapshot> CreateStoredBackupSnapshotAsync(CancellationToken ct) => CaptureAsync(storedBackup: true, ct);

    private async Task<TenantRestoreSnapshot> CaptureAsync(bool storedBackup, CancellationToken ct)
    {
        var mid = context.CurrentMunicipalityId;
        if (mid == Guid.Empty) throw new InvalidOperationException("A municipality is required to capture a backup.");
        var before = await schemaReader.ReadAsync(ct);
        if (!storedBackup) before.RequireRestorable();
        await using var snapshotTransaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var schemaState = await schemaReader.ReadAsync(ct);
        if (!storedBackup) schemaState.RequireRestorable();
        if (schemaState.Fingerprint != before.Fingerprint ||
            !schemaState.UnmanagedTables.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(before.UnmanagedTables.OrderBy(x => x, StringComparer.Ordinal)))
            throw new InvalidOperationException("The physical schema changed during backup capture.");
        var archiveOnly = storedBackup && schemaState.UnmanagedTables.Count != 0;
        var order = GetInsertOrder().Where(t => schemaState.Tables.ContainsKey(t.Table)).ToList();
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
                    cmd.Transaction = snapshotTransaction.GetDbTransaction();
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

        var snapshot = new TenantRestoreSnapshot(
            archiveOnly ? TenantRestoreSnapshot.ArchiveOnlyFormatVersion : TenantRestoreSnapshot.CurrentFormatVersion,
            muni?.TenantCode ?? "tenant",
            mid,
            DateTime.UtcNow,
            tables,
            schemaState.Fingerprint,
            archiveOnly ? schemaState.UnmanagedTables.ToArray() : null);
        if (archiveOnly) TenantRestoreValidation.ValidateArchive(snapshot, mid, schemaState);
        else TenantRestoreValidation.Validate(snapshot, mid, schemaState);
        await snapshotTransaction.CommitAsync(ct);
        return snapshot;
    }

    public async Task<TenantRestoreResult> RestoreAsync(TenantRestoreSnapshot snapshot, CancellationToken ct)
    {
        snapshot = TenantRestoreValidation.FreezeAndValidateHeader(snapshot, context.CurrentMunicipalityId);
        var schema = await schemaReader.ReadAsync(ct);
        TenantRestoreValidation.Validate(snapshot, context.CurrentMunicipalityId, schema);
        return await RestoreValidatedAsync(snapshot, ct);
    }

    // The destructive boundary is reached only after the server-owned manifest passes preflight.
    protected virtual async Task<TenantRestoreResult> RestoreValidatedAsync(TenantRestoreSnapshot snapshot, CancellationToken ct)
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

        // Serialize with the established migration runner and reject lock contention promptly.
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = dbTx;
            guard.CommandText = $"SELECT pg_try_advisory_xact_lock({DatabaseStartup.MigrationLockKey})";
            if (await guard.ExecuteScalarAsync(ct) is not true)
                throw new InvalidOperationException("A schema operation is in progress; restore was not started.");
        }
        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s';", ct);
        var schemaState = await schemaReader.ReadAsync(ct);
        TenantRestoreValidation.Validate(snapshot, mid, schemaState);
        order = order.Where(t => schemaState.Tables.ContainsKey(t.Table)).ToList();
        foreach (var (schema, table) in order)
        {
            await using var tableLock = connection.CreateCommand();
            tableLock.Transaction = dbTx;
            // Identifiers are exclusively from the EF model, not the uploaded table manifest.
            tableLock.CommandText = $"LOCK TABLE \"{schema}\".\"{table}\" IN SHARE ROW EXCLUSIVE MODE";
            await tableLock.ExecuteNonQueryAsync(ct);
        }
        schemaState = await schemaReader.ReadAsync(ct);
        TenantRestoreValidation.Validate(snapshot, mid, schemaState);

        // Cast every supplied value against its real PostgreSQL type before the first DELETE.
        // The pure validator has already checked row shape, nullability, IDs and tenant ownership.
        foreach (var (schema, table) in order)
        {
            await using var check = connection.CreateCommand();
            check.Transaction = dbTx;
            check.CommandText = $"SELECT * FROM json_populate_recordset(NULL::\"{schema}\".\"{table}\", @json::json)";
            AddParam(check, "json", snapshot.Tables[table]);
            await using var reader = await check.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }
        await ValidateTenantReferencesAsync(snapshot, connection, dbTx, mid, ct);

        // The append-only lifecycle tables refuse DELETE except under this transaction-local, tenant-restore-only switch.
        await using (var allow = connection.CreateCommand())
        {
            allow.Transaction = dbTx;
            allow.CommandText = "SELECT set_config('stalltrack.tenant_restore', 'on', true)";
            await allow.ExecuteNonQueryAsync(ct);
        }

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
            var json = snapshot.Tables[table];
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.GetArrayLength() == 0)
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

        // No setval/nextval: sequence changes do not roll back. Preflight rejects snapshots that
        // would require advancing the server-issued SRC high watermark.

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
        var audit = AuditLog.Create(
            actorId: currentUser.UserId?.ToString() ?? currentUser.Username ?? "system",
            actorName: currentUser.Username ?? "system",
            actorRole: currentUser.Role ?? "SuperAdmin",
            action: "TenantRestore",
            entityType: "Municipality",
            entityId: mid,
            newValues: newValues,
            notes: $"Restored {rows} row(s) across {tablesTouched} table(s) from a snapshot taken {snapshot.GeneratedAtUtc:u}.");
        context.AuditLogs.Add(audit);
        try
        {
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            // A failed restore must not leave an Added success audit available for a later SaveChanges.
            context.Entry(audit).State = EntityState.Detached;
            throw;
        }
        return new TenantRestoreResult(tablesTouched, rows, perTable);
    }

    /// <summary>Validate tenant-owned source references before changing any tenant rows.</summary>
    private async Task ValidateTenantReferencesAsync(TenantRestoreSnapshot snapshot, DbConnection connection,
        DbTransaction transaction, Guid mid, CancellationToken ct)
    {
        // Older source tables have single-ID FKs. A correct MunicipalityId on the child alone is
        // insufficient: never restore a child that points at another tenant's principal.
        foreach (var table in context.Model.GetRelationalModel().Tables.Where(t => snapshot.Tables.ContainsKey(t.Name)))
        foreach (var fk in table.ForeignKeyConstraints.Where(f => f.PrincipalTable.FindColumn("MunicipalityId") is not null))
        {
            var principal = fk.PrincipalTable;
            var parentFromSnapshot = snapshot.Tables.TryGetValue(principal.Name, out var parentJson);
            var source = parentFromSnapshot
                ? $"json_populate_recordset(NULL::\"{principal.Schema ?? "public"}\".\"{principal.Name}\", @parent::json)"
                : $"\"{principal.Schema ?? "public"}\".\"{principal.Name}\"";
            var equality = string.Join(" AND ", fk.Columns.Zip(fk.PrincipalColumns,
                (child, parent) => $"p.\"{parent.Name}\" = c.\"{child.Name}\""));
            var nonNull = string.Join(" AND ", fk.Columns.Select(c => $"c.\"{c.Name}\" IS NOT NULL"));
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT EXISTS (SELECT 1 FROM json_populate_recordset(NULL::\"{table.Schema ?? "public"}\".\"{table.Name}\", @child::json) c " +
                $"WHERE {nonNull} AND NOT EXISTS (SELECT 1 FROM {source} p WHERE {equality} AND p.\"MunicipalityId\"=@mid))";
            AddParam(command, "child", snapshot.Tables[table.Name]);
            if (parentFromSnapshot) AddParam(command, "parent", parentJson!);
            AddParam(command, "mid", mid);
            if (await command.ExecuteScalarAsync(ct) is true)
                throw new InvalidOperationException("The backup contains an unavailable or cross-tenant source relationship.");
        }
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
