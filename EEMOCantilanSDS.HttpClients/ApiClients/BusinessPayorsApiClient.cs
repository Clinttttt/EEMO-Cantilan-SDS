using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class BusinessPayorsApiClient(HttpClient http) : HandleResponse(http), IBusinessPayorsApiClient
{
    public Task<Result<IReadOnlyList<PayorOccupancyDto>>> GetOccupanciesAsync(string? search, PayorLinkFilter filter) =>
        GetAsync<IReadOnlyList<PayorOccupancyDto>>(
            $"api/business-payors/occupancies?filter={(int)filter}&search={Uri.EscapeDataString(search ?? string.Empty)}");

    public Task<Result<IReadOnlyList<PayorCandidateDto>>> SearchPayorsAsync(string search) =>
        GetAsync<IReadOnlyList<PayorCandidateDto>>($"api/business-payors/candidates?search={Uri.EscapeDataString(search)}");

    public Task<Result<PayorLinkOutcomeDto>> LinkAsync(LinkPayorRequest request) =>
        PostAsync<LinkPayorRequest, PayorLinkOutcomeDto>("api/business-payors/links", request);

    public Task<Result<PayorLinkOutcomeDto>> CreateAndLinkAsync(CreatePayorAndLinkRequest request) =>
        PostAsync<CreatePayorAndLinkRequest, PayorLinkOutcomeDto>("api/business-payors/creations", request);
}
