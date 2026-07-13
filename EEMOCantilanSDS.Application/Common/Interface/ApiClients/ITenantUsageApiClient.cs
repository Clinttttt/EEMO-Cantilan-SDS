using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Client-side facade over api/tenant-usage. Returns the storage footprint of the signed-in Head's own
/// municipality, and downloads a per-tenant data export. All access control and tenant scoping happen
/// server-side.
/// </summary>
public interface ITenantUsageApiClient
{
    Task<Result<TenantUsageDto>> GetUsageAsync();

    /// <summary>Downloads the caller's own municipality data export (JSON file bytes).</summary>
    Task<Result<BackupArtifact>> ExportAsync();

    /// <summary>Downloads the caller's own restore-ready snapshot (the file the scoped restore accepts).</summary>
    Task<Result<BackupArtifact>> DownloadRestoreSnapshotAsync();

    /// <summary>Restores the caller's own municipality from an uploaded snapshot (guarded server-side).</summary>
    Task<Result<TenantRestoreResult>> RestoreAsync(EEMOCantilanSDS.Application.Requests.Backup.TenantRestoreRequest request);

    // ── Stored backup history (per-municipality) ──

    /// <summary>Create + store a new backup of the caller's own municipality.</summary>
    Task<Result<TenantBackupInfo>> CreateBackupAsync(string? note = null);

    /// <summary>The caller's own stored backups (metadata only), newest first.</summary>
    Task<Result<IReadOnlyList<TenantBackupInfo>>> ListBackupsAsync();

    /// <summary>Downloads one of the caller's own stored backups as its restore-ready file.</summary>
    Task<Result<BackupArtifact>> DownloadBackupAsync(Guid id);

    /// <summary>The contents manifest (per-table record counts) of one of the caller's own stored backups.</summary>
    Task<Result<TenantBackupContentsDto>> GetBackupContentsAsync(Guid id);

    /// <summary>Restores the caller's own municipality from a stored backup (guarded server-side).</summary>
    Task<Result<TenantRestoreResult>> RestoreFromBackupAsync(Guid id, string confirmationPhrase, string password);

    /// <summary>Recent restore events for the caller's own municipality.</summary>
    Task<Result<IReadOnlyList<TenantRestoreEventDto>>> GetRestoreHistoryAsync();
}
