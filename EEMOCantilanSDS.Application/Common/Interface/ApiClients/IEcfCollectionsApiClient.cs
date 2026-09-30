using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IEcfCollectionsApiClient
{
    Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetObligationsAsync(int year, int month);
    Task<Result<EcfObligationQuoteDto>> GetObligationAsync(Guid utilityBillId);
    Task<Result<IReadOnlyList<EcfAvailableDocumentDto>>> GetAvailableReceiptsAsync();
    Task<Result<EcfCollectionDraftDto>> GetCurrentDraftAsync();
    Task<Result<EcfCollectionDraftDto>> GetDraftAsync(Guid draftId);
    Task<Result<EcfCollectionDraftDto>> ResumeDraftAsync(Guid draftId, EcfDraftRevisionRequest request);
    Task<Result<EcfCollectionDraftDto>> CreateDraftAsync(CreateEcfCollectionDraftRequest request);
    Task<Result<EcfCollectionDraftDto>> UpdateAllocationAsync(Guid draftId, UpdateEcfDraftAllocationRequest request);
    Task<Result<EcfCollectionDraftDto>> SelectDocumentAsync(Guid draftId, SelectEcfDraftDocumentRequest request);
    Task<Result<EcfCollectionDraftDto>> ReviewAsync(Guid draftId, EcfDraftRevisionRequest request);
    Task<Result<EcfCollectionDraftDto>> DiscardAsync(Guid draftId, EcfDraftRevisionRequest request);
    Task<Result<EcfPostOutcomeDto>> PostAsync(Guid draftId, PostEcfCollectionDraftRequest request);
    Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to);
    Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetCollectionActivityAsync(DateOnly from, DateOnly to);
    Task<Result<RentObligationQuoteDto>> GetRentObligationAsync(Guid stallId, int year, int month);
    Task<Result<IReadOnlyList<CollectionCandidateDto>>> GetPayorObligationsAsync(Guid payorId);
    Task<Result<EcfCollectionDraftDto>> AddEcfLineAsync(AddEcfDraftLineRequest request);
    Task<Result<EcfCollectionDraftDto>> AddRentAllocationAsync(AddRentDraftAllocationRequest request);
    Task<Result<EcfCollectionDraftDto>> UpdateDraftAllocationAsync(Guid draftId, UpdateCollectionDraftAllocationRequest request);
    Task<Result<IReadOnlyList<CollectionPayorDto>>> SearchCollectionPayorsAsync(string search);
}
