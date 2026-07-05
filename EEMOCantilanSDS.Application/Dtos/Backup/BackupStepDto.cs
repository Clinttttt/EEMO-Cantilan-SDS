namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// A single step within a GitHub Actions backup workflow job, surfaced to the Head-only Backups page
/// as one row of the in-app run "pipeline". Times are the raw UTC instants reported by GitHub; the
/// client renders durations and any timestamps in Philippine time.
/// </summary>
public record BackupStepDto(int Number, string Name, string Status, string? Conclusion, DateTime? StartedAt, DateTime? CompletedAt);
