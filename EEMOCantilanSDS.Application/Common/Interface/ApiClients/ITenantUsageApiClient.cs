using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Client-side facade over api/tenant-usage. Returns the storage footprint of the signed-in Head's own
/// municipality. All access control and tenant scoping happen server-side.
/// </summary>
public interface ITenantUsageApiClient
{
    Task<Result<TenantUsageDto>> GetUsageAsync();
}
