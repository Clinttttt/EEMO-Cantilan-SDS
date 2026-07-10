using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>
/// Client-side facade over api/tenant-usage. Returns the signed-in Head's own municipality storage
/// footprint. All access control and querying happen server-side (scoped to the caller's tenant).
/// </summary>
public class TenantUsageApiClient(HttpClient http) : HandleResponse(http), ITenantUsageApiClient
{
    public async Task<Result<TenantUsageDto>> GetUsageAsync() =>
        await GetAsync<TenantUsageDto>("api/tenant-usage");
}
