namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// A single GitHub Actions backup workflow run, surfaced to the Head-only Backups page.
/// Times are the raw UTC instants reported by GitHub; the client renders them in Philippine time.
/// </summary>
public record BackupRunDto(long Id, string Status, string? Conclusion, DateTime CreatedAt, string? Event, string HtmlUrl);
