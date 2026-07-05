namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// Detailed view of a single backup workflow run — the run-level summary plus its flattened step
/// timeline — powering the in-app "pipeline" modal on the Head-only Backups page. Times are the raw
/// UTC instants reported by GitHub; the client renders them in Philippine time.
/// </summary>
public record BackupRunDetailDto(
    long RunId,
    string Status,
    string? Conclusion,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Event,
    string HtmlUrl,
    IReadOnlyList<BackupStepDto> Steps);
