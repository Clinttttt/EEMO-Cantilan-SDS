using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view of approved penalty definitions and the register of posted fines (IA-049).</summary>
public interface IPenaltiesApiClient
{
    Task<Result<IReadOnlyList<PenaltyDefinitionDto>>> GetDefinitionsAsync();
    Task<Result<PenaltyDefinitionDto>> DefineAsync(DefinePenaltyRequest request);
    Task<Result<IReadOnlyList<PenaltyRegisterRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to);
    Task<Result<IReadOnlyList<FineAssessmentDto>>> GetAssessmentsAsync(FineAccountKind? kind = null, Guid? accountId = null, string? search = null);
    Task<Result<IReadOnlyList<FineAccountOption>>> SearchFineAccountsAsync(string search);
    Task<Result<FineAccountOption>> GetFineAccountAsync(FineAccountRef account);
    Task<Result<IReadOnlyList<FinePolicyDto>>> GetFinePoliciesAsync();
    Task<Result<FinePolicyDto>> RecordFinePolicyAsync(FinePolicyRequest request);
    Task<Result<FineAssessmentDto>> CreateAssessmentAsync(CreateFineAssessmentRequest request);
    Task<Result<IReadOnlyList<FineAssessmentEventDto>>> GetAssessmentHistoryAsync(Guid id);
}
