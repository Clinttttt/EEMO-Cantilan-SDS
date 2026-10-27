using EEMOCantilanSDS.Application.Dtos.Backup;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Faithful per-municipality snapshot + atomic scoped restore. Both operations act ONLY on the caller's
/// own tenant (<see cref="AppDbContext.CurrentMunicipalityId"/>); the restore is a single transaction so
/// any failure rolls back with zero changes, and it never touches another municipality's rows.
/// </summary>
public interface ITenantRestoreRepository
{
    /// <summary>Capture a round-trippable snapshot of the caller's municipality (all restorable tables).</summary>
    Task<TenantRestoreSnapshot> CreateSnapshotAsync(CancellationToken ct);

    /// <summary>
    /// Capture a stored backup: restore-v2 when the physical schema is fully supported, otherwise
    /// an explicitly non-restorable archive-v1 of only approved mapped tenant tables.
    /// Never relaxes CreateSnapshotAsync or RestoreAsync safety rules.
    /// </summary>
    Task<TenantRestoreSnapshot> CreateStoredBackupSnapshotAsync(CancellationToken ct);

    /// <summary>
    /// Atomically replace the caller's municipality data with <paramref name="snapshot"/> — DELETE the
    /// tenant's rows then re-insert from the snapshot, in FK order, in ONE transaction. Rejects a snapshot
    /// whose <see cref="TenantRestoreSnapshot.MunicipalityId"/> is not the caller's own tenant. The audit
    /// log is never deleted/overwritten (it is append-only).
    /// </summary>
    Task<TenantRestoreResult> RestoreAsync(TenantRestoreSnapshot snapshot, CancellationToken ct);
}
