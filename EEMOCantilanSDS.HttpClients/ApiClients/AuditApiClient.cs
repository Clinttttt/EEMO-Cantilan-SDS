using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Audit;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class AuditApiClient(HttpClient http) : HandleResponse(http), IAuditApiClient
{
    public async Task<Result<AuditTrailDto>> GetAuditTrailAsync(
        string? search = null,
        string? action = null,
        string? entityType = null,
        string? actor = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int page = 1,
        int pageSize = 25,
        bool includeOptions = true)
    {
        var queryParams = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
            $"includeOptions={includeOptions.ToString().ToLowerInvariant()}"
        };

        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(action))
            queryParams.Add($"action={Uri.EscapeDataString(action)}");
        if (!string.IsNullOrWhiteSpace(entityType))
            queryParams.Add($"entityType={Uri.EscapeDataString(entityType)}");
        if (!string.IsNullOrWhiteSpace(actor))
            queryParams.Add($"actor={Uri.EscapeDataString(actor)}");
        if (fromUtc.HasValue)
            queryParams.Add($"fromUtc={Uri.EscapeDataString(fromUtc.Value.ToString("O"))}");
        if (toUtc.HasValue)
            queryParams.Add($"toUtc={Uri.EscapeDataString(toUtc.Value.ToString("O"))}");

        var url = "api/Audit/trail?" + string.Join("&", queryParams);
        return await GetAsync<AuditTrailDto>(url);
    }
}
