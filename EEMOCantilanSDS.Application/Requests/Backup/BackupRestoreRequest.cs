namespace EEMOCantilanSDS.Application.Requests.Backup;

/// <summary>Body for restoring the caller's municipality from a STORED backup (the id is in the route).</summary>
public record BackupRestoreRequest(string ConfirmationPhrase, string Password);
