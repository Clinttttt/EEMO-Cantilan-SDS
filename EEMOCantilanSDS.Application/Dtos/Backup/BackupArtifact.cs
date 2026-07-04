namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// The downloaded backup artifact (a zip of the database dump) streamed back through the API.
/// The bytes never touch the browser directly — the API proxies them so the GitHub token stays server-side.
/// </summary>
public record BackupArtifact(string FileName, byte[] Content, string ContentType);
