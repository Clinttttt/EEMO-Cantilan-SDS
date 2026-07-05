using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Client-side facade over the api/backup/* endpoints for the Head-only Backups page.
/// The client only ever talks to our own API; the GitHub token lives strictly server-side.
/// </summary>
public interface IBackupApiClient
{
    Task<Result<bool>> TriggerBackupAsync();
    Task<Result<bool>> TriggerRestoreAsync(string confirmationPhrase, string password);
    Task<Result<IReadOnlyList<BackupRunDto>>> GetRecentRunsAsync();
    Task<Result<BackupRunDetailDto>> GetRunDetailAsync(long runId);
    Task<Result<BackupArtifact>> GetLatestAsync();
}
