namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// A faithful, round-trippable snapshot of ONE municipality's restorable data. Each table's rows are kept
/// as raw JSON (Postgres <c>row_to_json</c>) so a restore is column-for-column identical (via
/// <c>json_populate_recordset</c>) — no lossy domain-entity reconstruction. Credentials and the audit log
/// are intentionally excluded. This is the ONLY format the scoped restore accepts.
/// </summary>
public sealed record TenantRestoreSnapshot(
    string FormatVersion,
    string TenantCode,
    Guid MunicipalityId,
    DateTime GeneratedAtUtc,
    // tableName -> JSON array text of that table's rows for this municipality
    Dictionary<string, string> Tables)
{
    public const string CurrentFormatVersion = "restore-v1";
}

/// <summary>Outcome of a scoped restore: total rows written and per-table counts (for the audit + UI).</summary>
public sealed record TenantRestoreResult(int TablesRestored, int RowsRestored, IReadOnlyDictionary<string, int> RowsPerTable);
