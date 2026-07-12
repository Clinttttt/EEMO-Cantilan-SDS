namespace EEMOCantilanSDS.Application.Requests.Backup;

/// <summary>Upload payload for a scoped per-municipality restore: the snapshot file (base64) + guards.</summary>
public record TenantRestoreRequest(string SnapshotBase64, string ConfirmationPhrase, string Password);
