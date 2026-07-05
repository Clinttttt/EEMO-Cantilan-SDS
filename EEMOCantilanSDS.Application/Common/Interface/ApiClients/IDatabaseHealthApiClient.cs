using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Client-side facade over api/database-health. Talks only to our own API; the database connection
/// string and credentials never reach the browser.
/// </summary>
public interface IDatabaseHealthApiClient
{
    Task<Result<DatabaseHealthDto>> GetHealthAsync();
}
