using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class CollectionActivityApiClient(HttpClient http) : HandleResponse(http), ICollectionActivityApiClient
{
    public Task<Result<CollectionReconciliationDto>> GetReconciliationAsync(DateOnly from, DateOnly to,
        CollectionReconciliationBasis basis, DateTimeOffset? asOf = null, int limit = 500) =>
        GetAsync<CollectionReconciliationDto>($"api/collections/activity/reconciliation?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&basis={(int)basis}&limit={limit}"
            + (asOf is { } cutoff ? "&asOf=" + Uri.EscapeDataString(cutoff.ToString("O", CultureInfo.InvariantCulture)) : ""));
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
    public Task<Result<CollectionReviewDto>> GetReviewAsync(Guid collectionId) =>
        GetAsync<CollectionReviewDto>($"api/collections/activity/{collectionId}/review");
    public Task<Result<HeadCorrectionResultDto>> ReverseAsync(HeadReverseCollectionRequest request) =>
        PostAsync<HeadReverseCollectionRequest, HeadCorrectionResultDto>("api/collections/activity/reversals", request);
}
