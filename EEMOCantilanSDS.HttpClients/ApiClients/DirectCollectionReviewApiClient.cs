using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class DirectCollectionReviewApiClient(HttpClient http) : HandleResponse(http), IDirectCollectionReviewApiClient
{
    public Task<Result<DirectCollectionReviewPageDto>> GetAsync(DirectCollectionReviewRequest r)
    {
        var query = new List<string>
        {
            $"state={Uri.EscapeDataString(r.State)}", $"skip={r.Skip.ToString(CultureInfo.InvariantCulture)}", $"take={r.Take.ToString(CultureInfo.InvariantCulture)}"
        };
        if (r.From is { } from) query.Add($"from={from:yyyy-MM-dd}");
        if (r.To is { } to) query.Add($"to={to:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(r.Operation)) query.Add($"operation={Uri.EscapeDataString(r.Operation.Trim())}");
        if (!string.IsNullOrWhiteSpace(r.Collector)) query.Add($"collector={Uri.EscapeDataString(r.Collector.Trim())}");
        if (!string.IsNullOrWhiteSpace(r.Search)) query.Add($"search={Uri.EscapeDataString(r.Search.Trim())}");
        return GetAsync<DirectCollectionReviewPageDto>("api/direct-collection-review?" + string.Join("&", query));
    }

    public Task<Result<DirectCollectionDetailDto>> DetailAsync(Guid collectionId) =>
        GetAsync<DirectCollectionDetailDto>($"api/direct-collection-review/{collectionId}");

    public Task<Result<DirectCollectionReviewRowDto>> ReviewAsync(DirectCollectionReviewActionRequest request) =>
        PostAsync<DirectCollectionReviewActionRequest, DirectCollectionReviewRowDto>("api/direct-collection-review/review", request);

    public Task<Result<DirectCollectionBulkConfirmResult>> ConfirmManyAsync(DirectCollectionBulkConfirmRequest request) =>
        PostAsync<DirectCollectionBulkConfirmRequest, DirectCollectionBulkConfirmResult>("api/direct-collection-review/confirm-many", request);
}
