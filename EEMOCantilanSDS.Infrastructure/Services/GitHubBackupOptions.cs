namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Bound from the "GitHubBackup" configuration section. The <see cref="Token"/> is a GitHub PAT/App
/// token with workflow + actions scope; it is applied to the HttpClient's Authorization header once at
/// registration and is NEVER returned to the client or written to any log.
/// </summary>
public class GitHubBackupOptions
{
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string WorkflowFileName { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
