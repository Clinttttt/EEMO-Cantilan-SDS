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
}
