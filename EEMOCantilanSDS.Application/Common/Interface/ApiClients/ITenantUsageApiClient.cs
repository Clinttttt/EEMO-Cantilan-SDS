using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Client-side facade over api/tenant-usage. Returns the storage footprint of the signed-in Head's own
/// municipality, and downloads a per-tenant data export. All access control and tenant scoping happen
/// server-side.
/// </summary>
public interface ITenantUsageApiClient
{
    Task<Result<TenantUsageDto>> GetUsageAsync();

    /// <summary>Downloads the caller's own municipality data export (JSON file bytes).</summary>
    Task<Result<BackupArtifact>> ExportAsync();
}
