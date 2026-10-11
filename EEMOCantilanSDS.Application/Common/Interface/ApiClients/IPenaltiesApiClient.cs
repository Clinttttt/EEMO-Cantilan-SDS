using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view of approved penalty definitions and the register of posted fines (IA-049).</summary>
public interface IPenaltiesApiClient
{
    Task<Result<DirectFineOptionsDto>> GetDirectFineOptionsAsync();
    Task<Result<DirectFineQuoteDto>> QuoteDirectFineAsync(DirectFineCollectionRequest request);
    Task<Result<GovernedServiceOutcomeDto>> PostDirectFineAsync(DirectFineCollectionRequest request);
    Task<Result<IReadOnlyList<PenaltyDefinitionDto>>> GetDefinitionsAsync();
    Task<Result<PenaltyDefinitionDto>> DefineAsync(DefinePenaltyRequest request);
    Task<Result<IReadOnlyList<PenaltyRegisterRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to);
    Task<Result<PenaltyWorkspaceSummaryDto>> GetSummaryAsync(DateOnly from, DateOnly to);
    Task<Result<IReadOnlyList<FineAssessmentDto>>> GetAssessmentsAsync(FineAccountKind? kind = null, Guid? accountId = null, string? search = null);
    Task<Result<IReadOnlyList<FineAccountOption>>> SearchFineAccountsAsync(string search);
    Task<Result<FineAccountOption>> GetFineAccountAsync(FineAccountRef account);
    Task<Result<FineAccountOption>> ResolveFacilityFineAccountAsync(Guid stallId, EEMOCantilanSDS.Domain.Enums.FacilityCode facility, DateOnly snapshotDate, Guid? contractId = null);
    Task<Result<IReadOnlyList<FineAccountOption>>> GetFacilityFineAccountsAsync(Guid stallId, EEMOCantilanSDS.Domain.Enums.FacilityCode facility);
    Task<Result<IReadOnlyList<FinePeriodOption>>> GetFinePeriodsAsync(FineAccountRef account, int? year = null);
    Task<Result<IReadOnlyList<FineSourceOption>>> GetFineSourcesAsync();
    Task<Result<IReadOnlyList<FinePolicyDto>>> GetFinePoliciesAsync();
    Task<Result<FinePolicyDto>> RecordFinePolicyAsync(FinePolicyRequest request);
    Task<Result<FineAssessmentDto>> CreateAssessmentAsync(CreateFineAssessmentRequest request);
    Task<Result<IReadOnlyList<FineAssessmentEventDto>>> GetAssessmentHistoryAsync(Guid id);
    Task<Result<FineSettlementQuoteDto>> QuoteFineAsync(Guid id);
    Task<Result<IReadOnlyList<FineSettlementHistoryDto>>> GetFineSettlementHistoryAsync(Guid id);
    Task<Result<EcfCollectionDraftDto>> GetCurrentFineDraftAsync(Guid assessmentId);
    Task<Result<EcfCollectionDraftDto>> AddFineDraftAsync(AddFineAssessmentDraftRequest request);
    Task<Result<EcfCollectionDraftDto>> ReviewFineDraftAsync(Guid id, EcfDraftRevisionRequest request);
    Task<Result<EcfCollectionDraftDto>> DiscardFineDraftAsync(Guid id, EcfDraftRevisionRequest request);
    Task<Result<EcfPostOutcomeDto>> PostFineDraftAsync(Guid id, PostEcfCollectionDraftRequest request);
}
