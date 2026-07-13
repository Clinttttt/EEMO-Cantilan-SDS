using EEMOCantilanSDS.Application.Dtos.Backup;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Per-municipality stored backup history — the scoped equivalent of the platform's whole-database
/// backups. Every operation acts ONLY on the caller's own tenant (the global query filter scopes reads,
/// and new rows are stamped with the caller's MunicipalityId). Creating a backup captures a faithful
/// round-trippable snapshot; restoring from one reuses the atomic scoped restore.
/// </summary>
public interface ITenantBackupRepository
{
    /// <summary>Capture and store a new backup of the caller's municipality; enforces retention (keep last N).</summary>
    Task<TenantBackupInfo> CreateAsync(string? note, CancellationToken ct);

    /// <summary>The caller's stored backups, newest first (metadata only — no payload).</summary>
    Task<IReadOnlyList<TenantBackupInfo>> ListAsync(CancellationToken ct);

    /// <summary>The raw snapshot bytes + metadata for one stored backup (scoped to the caller), for download.</summary>
    Task<(TenantBackupInfo Info, byte[] Bytes)?> GetFileAsync(Guid id, CancellationToken ct);

    /// <summary>The deserialized snapshot for one stored backup (scoped to the caller), for restore.</summary>
    Task<TenantRestoreSnapshot?> GetSnapshotAsync(Guid id, CancellationToken ct);

    /// <summary>The inspectable contents (per-table record counts) of one stored backup, scoped to the caller.</summary>
    Task<TenantBackupContentsDto?> GetContentsAsync(Guid id, CancellationToken ct);

    /// <summary>The actual records of one table inside a stored backup (columns + string values), capped at <paramref name="max"/>.</summary>
    Task<TenantBackupTableRowsDto?> GetTableRowsAsync(Guid id, string table, int max, CancellationToken ct);

    /// <summary>Recent restore events for the caller's municipality, newest first (from the audit log).</summary>
    Task<IReadOnlyList<TenantRestoreEventDto>> ListRestoreEventsAsync(int take, CancellationToken ct);
}
