using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Server-side gateway to the GitHub Actions backup workflow. Implementations hold the GitHub token
/// (never exposed to the client) and translate GitHub REST responses into the app's Result pattern.
/// </summary>
public interface IBackupService
{
    /// <summary>Dispatch the backup workflow (workflow_dispatch) on the configured ref.</summary>
    Task<Result<bool>> TriggerBackupAsync(CancellationToken ct);

    /// <summary>List the most recent backup workflow runs, newest first.</summary>
    Task<Result<IReadOnlyList<BackupRunDto>>> GetRecentRunsAsync(int count, CancellationToken ct);

    /// <summary>Download the artifact of the newest successful backup run.</summary>
    Task<Result<BackupArtifact>> GetLatestArtifactAsync(CancellationToken ct);
}
