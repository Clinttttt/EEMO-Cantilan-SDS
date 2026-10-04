using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class EcfCollectionsApiClient(HttpClient http) : HandleResponse(http), IEcfCollectionsApiClient
{
    public Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetObligationsAsync(int year, int month) =>
        GetAsync<IReadOnlyList<EcfObligationQuoteDto>>($"api/ecf-collections/obligations?year={year}&month={month}");

    public Task<Result<EcfObligationQuoteDto>> GetObligationAsync(Guid utilityBillId) =>
        GetAsync<EcfObligationQuoteDto>($"api/ecf-collections/obligations/{utilityBillId}");

    public Task<Result<EcfCollectionDraftDto>> GetCurrentDraftAsync() =>
        GetAsync<EcfCollectionDraftDto>("api/ecf-collections/drafts/current");

    public Task<Result<EcfCollectionDraftDto>> GetDraftAsync(Guid draftId) =>
        GetAsync<EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}");

    public Task<Result<EcfCollectionDraftDto>> ResumeDraftAsync(Guid draftId, EcfDraftRevisionRequest request) =>
        PostAsync<EcfDraftRevisionRequest, EcfCollectionDraftDto>(
            $"api/collections/composer/drafts/{draftId}/resume", request);

    public Task<Result<EcfCollectionDraftDto>> CreateDraftAsync(CreateEcfCollectionDraftRequest request) =>
        PostAsync<CreateEcfCollectionDraftRequest, EcfCollectionDraftDto>("api/ecf-collections/drafts", request);

    public Task<Result<EcfCollectionDraftDto>> UpdateAllocationAsync(Guid draftId, UpdateEcfDraftAllocationRequest request) =>
        PutAsync<UpdateEcfDraftAllocationRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/allocation", request);

    public Task<Result<EcfCollectionDraftDto>> ReviewAsync(Guid draftId, EcfDraftRevisionRequest request) =>
        PostAsync<EcfDraftRevisionRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/review", request);

    public Task<Result<EcfCollectionDraftDto>> DiscardAsync(Guid draftId, EcfDraftRevisionRequest request) =>
        PostAsync<EcfDraftRevisionRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/discard", request);

    public Task<Result<EcfPostOutcomeDto>> PostAsync(Guid draftId, PostEcfCollectionDraftRequest request) =>
        PostAsync<PostEcfCollectionDraftRequest, EcfPostOutcomeDto>($"api/ecf-collections/drafts/{draftId}/post", request);

    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<EcfCollectionActivityDto>>(
            $"api/ecf-collections/activity?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetCollectionActivityAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<EcfCollectionActivityDto>>(
            $"api/collections/composer/activity?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

    public Task<Result<RentObligationQuoteDto>> GetRentObligationAsync(Guid stallId, int year, int month) =>
        GetAsync<RentObligationQuoteDto>(
            $"api/collections/composer/rent-obligation?stallId={stallId}&year={year}&month={month}");

    public Task<Result<IReadOnlyList<CollectionCandidateDto>>> GetPayorObligationsAsync(Guid payorId) =>
        GetAsync<IReadOnlyList<CollectionCandidateDto>>($"api/collections/composer/payors/{payorId}/obligations");

    public Task<Result<EcfCollectionDraftDto>> AddEcfLineAsync(AddEcfDraftLineRequest request) =>
        PostAsync<AddEcfDraftLineRequest, EcfCollectionDraftDto>("api/collections/composer/drafts/ecf-lines", request);

    public Task<Result<EcfCollectionDraftDto>> AddPenaltyLineAsync(AddPenaltyDraftLineRequest request) =>
        PostAsync<AddPenaltyDraftLineRequest, EcfCollectionDraftDto>(
            "api/collections/composer/drafts/penalty-lines", request);

    public Task<Result<EcfCollectionDraftDto>> AddObligationAllocationAsync(AddObligationDraftAllocationRequest request) =>
        PostAsync<AddObligationDraftAllocationRequest, EcfCollectionDraftDto>(
            "api/collections/composer/drafts/obligation-allocations", request);

    public Task<Result<EcfCollectionDraftDto>> AddRentAllocationAsync(AddRentDraftAllocationRequest request) =>
        PostAsync<AddRentDraftAllocationRequest, EcfCollectionDraftDto>(
            "api/collections/composer/drafts/rent-allocations", request);

    public Task<Result<EcfCollectionDraftDto>> UpdateDraftAllocationAsync(
        Guid draftId, UpdateCollectionDraftAllocationRequest request) =>
        PutAsync<UpdateCollectionDraftAllocationRequest, EcfCollectionDraftDto>(
            $"api/collections/composer/drafts/{draftId}/allocations", request);

    public Task<Result<IReadOnlyList<CollectionPayorDto>>> SearchCollectionPayorsAsync(string search) =>
        GetAsync<IReadOnlyList<CollectionPayorDto>>(
            $"api/collections/composer/payors?search={Uri.EscapeDataString(search)}");
}
