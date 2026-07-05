using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>
/// Client-side facade over api/database-health. Returns a live PostgreSQL health snapshot for the
/// Head/Admin-only Settings panel. All access control and querying happens server-side.
/// </summary>
public class DatabaseHealthApiClient(HttpClient http) : HandleResponse(http), IDatabaseHealthApiClient
{
    public async Task<Result<DatabaseHealthDto>> GetHealthAsync() =>
        await GetAsync<DatabaseHealthDto>("api/database-health");
}
