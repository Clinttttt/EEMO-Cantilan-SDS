using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class CollectionActivityApiClient(HttpClient http) : HandleResponse(http), ICollectionActivityApiClient
{
    public Task<Result<CollectionActivityFeedDto>> GetAsync(DateOnly from, DateOnly to, FacilityCode? facility = null,
        Guid? collectorId = null, string? authority = null, int limit = 500, string? reference = null)
    {
        var query = $"api/collections/activity?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            + $"&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&limit={limit}";
        if (facility is not null) query += $"&facility={facility}";
        if (collectorId is not null) query += $"&collectorId={collectorId}";
        if (!string.IsNullOrWhiteSpace(authority)) query += $"&authority={Uri.EscapeDataString(authority)}";
        if (!string.IsNullOrWhiteSpace(reference)) query += $"&reference={Uri.EscapeDataString(reference)}";
        return GetAsync<CollectionActivityFeedDto>(query);
    }
}
