using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>
/// Client-side facade over api/backup/*. Trigger + runs use the JSON helpers; the artifact download
/// reads raw bytes off the response (the API streams a zip, not JSON). The GitHub token is never seen here.
/// </summary>
public class BackupApiClient(HttpClient http) : HandleResponse(http), IBackupApiClient
{
    private readonly HttpClient _http = http;

    public async Task<Result<bool>> TriggerBackupAsync() =>
        await PostAsync<bool>("api/backup/run");

    public async Task<Result<bool>> TriggerRestoreAsync(string confirmationPhrase, string password) =>
        await PostAsync<object, bool>("api/backup/restore",
            new { ConfirmationPhrase = confirmationPhrase, Password = password });

    public async Task<Result<IReadOnlyList<BackupRunDto>>> GetRecentRunsAsync() =>
        await GetAsync<IReadOnlyList<BackupRunDto>>("api/backup/runs");

    public async Task<Result<BackupRunDetailDto>> GetRunDetailAsync(long runId) =>
        await GetAsync<BackupRunDetailDto>($"api/backup/runs/{runId}");

    public async Task<Result<BackupArtifact>> GetLatestAsync()
    {
        var resp = await _http.GetAsync("api/backup/latest");
        if (!resp.IsSuccessStatusCode)
            return Result<BackupArtifact>.Failure("No backup artifact available yet.", (int)resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();

        var contentDisposition = resp.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar
            ?? contentDisposition?.FileName?.Trim('"')
            ?? "stalltrack-backup.zip";

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/zip";

        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, bytes, contentType));
    }
}
