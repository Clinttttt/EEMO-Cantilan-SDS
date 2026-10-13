using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class FishMeatPayorFollowUpApiClient(HttpClient http) : HandleResponse(http), IFishMeatPayorFollowUpApiClient
{
    public Task<Result<FishMeatPayorFollowUpPageDto>> ListAsync(string? search, int skip, int take)
    {
        var query = $"skip={skip.ToString(CultureInfo.InvariantCulture)}&take={take.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search.Trim())}";
        return GetAsync<FishMeatPayorFollowUpPageDto>("api/fish-meat-payor-follow-up?" + query);
    }

    public Task<Result<FishMeatPayorFollowUpDetailDto>> DetailAsync(Guid collectionId) =>
        GetAsync<FishMeatPayorFollowUpDetailDto>($"api/fish-meat-payor-follow-up/{collectionId}");

    public Task<Result<FishMeatPayorResolveResult>> ResolveAsync(FishMeatPayorResolveRequest request) =>
        PostAsync<FishMeatPayorResolveRequest, FishMeatPayorResolveResult>("api/fish-meat-payor-follow-up/resolve", request);
}
