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
    // The vetted set of restorable tenant tables — MUST mirror the export (credentials, users and the
    // append-only audit log are intentionally excluded). Order here is irrelevant; FK order is derived.
    private static readonly HashSet<string> RestorableTables = new(StringComparer.Ordinal)
    {
        "Facilities", "FacilityRates", "OrSeriesConfigs", "Stalls", "Contracts", "PaymentRecords",
        "DailyCollections", "UtilityBills", "StallMonthlyExceptions", "NpmMarketClosures",
        "OnlinePaymentTransactions", "SlaughterTransactions", "SlaughterAnimalRates", "TpmVendors",
        "TpmAttendances", "TrmTransporters", "TrmTrips", "PayorStallLinks", "CollectorFacilityAssignments",
    };

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

        // 2) Re-insert from the snapshot, parents → children, verbatim (json_populate_recordset).
        foreach (var (schema, table) in order)
        {
            if (!snapshot.Tables.TryGetValue(table, out var json) || string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                perTable[table] = 0;
                continue;
            }

            await using var ins = connection.CreateCommand();
            ins.Transaction = dbTx;
            ins.CommandText =
                $"INSERT INTO \"{schema}\".\"{table}\" " +
                $"SELECT * FROM json_populate_recordset(NULL::\"{schema}\".\"{table}\", @json::json);";
            AddParam(ins, "json", json);
            perTable[table] = await ins.ExecuteNonQueryAsync(ct);
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
