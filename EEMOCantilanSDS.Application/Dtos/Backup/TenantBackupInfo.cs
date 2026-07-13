namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>Metadata for one stored per-municipality backup (no payload) — drives the "Recent backups" list.</summary>
public sealed record TenantBackupInfo(
    Guid Id,
    DateTime CreatedAtUtc,
    string CreatedBy,
    int RowCount,
    int TableCount,
    long SizeBytes,
    string? Note);

/// <summary>One entry in the per-municipality restore history (derived from the append-only audit log).</summary>
public sealed record TenantRestoreEventDto(
    DateTime WhenUtc,
    string By,
    string Summary,
    int RowsRestored = 0,
    int TablesRestored = 0,
    DateTime? SnapshotUtc = null,
    IReadOnlyList<TenantBackupTableDto>? Tables = null);
