using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Backup.RestoreTenantFromBackup;

/// <summary>
/// Head-triggered scoped restore of the caller's OWN municipality from a STORED backup (by id). The
/// confirmation phrase and admin password are re-verified server-side; the restore itself is a single
/// transaction scoped to the caller's tenant (any failure = zero changes). Mirrors the upload-restore
/// guards but sources the snapshot from the stored backup history instead of an uploaded file.
/// </summary>
public record RestoreTenantFromBackupCommand(Guid BackupId, string ConfirmationPhrase, string Password)
    : IRequest<Result<TenantRestoreResult>>;
