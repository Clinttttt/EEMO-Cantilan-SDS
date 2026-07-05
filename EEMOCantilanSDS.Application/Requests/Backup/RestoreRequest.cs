namespace EEMOCantilanSDS.Application.Requests.Backup;

/// <summary>
/// Body for the Head-only restore endpoint. Both fields are re-verified server-side (exact "RESTORE"
/// phrase and the admin's password) before the destructive restore workflow is dispatched.
/// </summary>
public record RestoreRequest(string ConfirmationPhrase, string Password);
