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

    public Task<Result<IReadOnlyList<EcfAvailableDocumentDto>>> GetAvailableReceiptsAsync() =>
        GetAsync<IReadOnlyList<EcfAvailableDocumentDto>>("api/ecf-collections/official-receipts/available");

    public Task<Result<EcfCollectionDraftDto>> GetCurrentDraftAsync() =>
        GetAsync<EcfCollectionDraftDto>("api/ecf-collections/drafts/current");

    public Task<Result<EcfCollectionDraftDto>> GetDraftAsync(Guid draftId) =>
        GetAsync<EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}");

    public Task<Result<EcfCollectionDraftDto>> CreateDraftAsync(CreateEcfCollectionDraftRequest request) =>
        PostAsync<CreateEcfCollectionDraftRequest, EcfCollectionDraftDto>("api/ecf-collections/drafts", request);

    public Task<Result<EcfCollectionDraftDto>> UpdateAllocationAsync(Guid draftId, UpdateEcfDraftAllocationRequest request) =>
        PutAsync<UpdateEcfDraftAllocationRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/allocation", request);

    public Task<Result<EcfCollectionDraftDto>> SelectDocumentAsync(Guid draftId, SelectEcfDraftDocumentRequest request) =>
        PutAsync<SelectEcfDraftDocumentRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/document", request);

    public Task<Result<EcfCollectionDraftDto>> ReviewAsync(Guid draftId, EcfDraftRevisionRequest request) =>
        PostAsync<EcfDraftRevisionRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/review", request);

    public Task<Result<EcfCollectionDraftDto>> DiscardAsync(Guid draftId, EcfDraftRevisionRequest request) =>
        PostAsync<EcfDraftRevisionRequest, EcfCollectionDraftDto>($"api/ecf-collections/drafts/{draftId}/discard", request);

    public Task<Result<EcfPostOutcomeDto>> PostAsync(Guid draftId, PostEcfCollectionDraftRequest request) =>
        PostAsync<PostEcfCollectionDraftRequest, EcfPostOutcomeDto>($"api/ecf-collections/drafts/{draftId}/post", request);

    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<EcfCollectionActivityDto>>(
            $"api/ecf-collections/activity?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
}
