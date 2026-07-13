using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>
/// Client-side facade over api/tenant-usage. Returns the signed-in Head's own municipality storage
/// footprint and downloads its data export. All access control and querying happen server-side (scoped
/// to the caller's tenant).
/// </summary>
public class TenantUsageApiClient(HttpClient http) : HandleResponse(http), ITenantUsageApiClient
{
    private readonly HttpClient _http = http;

    public async Task<Result<TenantUsageDto>> GetUsageAsync() =>
        await GetAsync<TenantUsageDto>("api/tenant-usage");

    public async Task<Result<BackupArtifact>> ExportAsync()
    {
        // The API streams a JSON file (not a Result envelope), so read the raw bytes off the response.
        var resp = await _http.GetAsync("api/tenant-usage/export");
        if (!resp.IsSuccessStatusCode)
            return Result<BackupArtifact>.Failure("Your data export is currently unavailable.", (int)resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var cd = resp.Content.Headers.ContentDisposition;
        var fileName = cd?.FileNameStar ?? cd?.FileName?.Trim('"') ?? "stalltrack-export.json";
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/json";

        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, bytes, contentType));
    }

    public async Task<Result<BackupArtifact>> DownloadRestoreSnapshotAsync()
    {
        var resp = await _http.GetAsync("api/tenant-usage/restore-snapshot");
        if (!resp.IsSuccessStatusCode)
            return Result<BackupArtifact>.Failure("The restore snapshot is currently unavailable.", (int)resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var cd = resp.Content.Headers.ContentDisposition;
        var fileName = cd?.FileNameStar ?? cd?.FileName?.Trim('"') ?? "stalltrack-restore.json";
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/json";

        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, bytes, contentType));
    }

    public async Task<Result<TenantRestoreResult>> RestoreAsync(EEMOCantilanSDS.Application.Requests.Backup.TenantRestoreRequest request) =>
        await PostAsync<EEMOCantilanSDS.Application.Requests.Backup.TenantRestoreRequest, TenantRestoreResult>("api/tenant-usage/restore", request);

    public async Task<Result<TenantBackupInfo>> CreateBackupAsync(string? note = null) =>
        await PostAsync<EEMOCantilanSDS.Application.Command.Backup.CreateTenantBackup.CreateTenantBackupCommand, TenantBackupInfo>(
            "api/tenant-usage/backups",
            new EEMOCantilanSDS.Application.Command.Backup.CreateTenantBackup.CreateTenantBackupCommand(note));

    public async Task<Result<IReadOnlyList<TenantBackupInfo>>> ListBackupsAsync() =>
        await GetAsync<IReadOnlyList<TenantBackupInfo>>("api/tenant-usage/backups");

    public async Task<Result<BackupArtifact>> DownloadBackupAsync(Guid id)
    {
        var resp = await _http.GetAsync($"api/tenant-usage/backups/{id}/file");
        if (!resp.IsSuccessStatusCode)
            return Result<BackupArtifact>.Failure("That backup could not be downloaded.", (int)resp.StatusCode);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var cd = resp.Content.Headers.ContentDisposition;
        var fileName = cd?.FileNameStar ?? cd?.FileName?.Trim('"') ?? "stalltrack-backup.json";
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/json";

        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, bytes, contentType));
    }

    public async Task<Result<TenantBackupContentsDto>> GetBackupContentsAsync(Guid id) =>
        await GetAsync<TenantBackupContentsDto>($"api/tenant-usage/backups/{id}/contents");

    public async Task<Result<TenantBackupTableRowsDto>> GetBackupTableRowsAsync(Guid id, string table) =>
        await GetAsync<TenantBackupTableRowsDto>($"api/tenant-usage/backups/{id}/tables/{Uri.EscapeDataString(table)}/rows");

    public async Task<Result<TenantRestoreResult>> RestoreFromBackupAsync(Guid id, string confirmationPhrase, string password) =>
        await PostAsync<EEMOCantilanSDS.Application.Requests.Backup.BackupRestoreRequest, TenantRestoreResult>(
            $"api/tenant-usage/backups/{id}/restore",
            new EEMOCantilanSDS.Application.Requests.Backup.BackupRestoreRequest(confirmationPhrase, password));

    public async Task<Result<IReadOnlyList<TenantRestoreEventDto>>> GetRestoreHistoryAsync() =>
        await GetAsync<IReadOnlyList<TenantRestoreEventDto>>("api/tenant-usage/restore-history");
}
