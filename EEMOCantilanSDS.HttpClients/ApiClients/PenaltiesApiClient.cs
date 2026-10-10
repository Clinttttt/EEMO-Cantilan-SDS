using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class PenaltiesApiClient(HttpClient http) : HandleResponse(http), IPenaltiesApiClient
{
    public Task<Result<FineSettlementQuoteDto>> QuoteFineAsync(Guid id) => GetAsync<FineSettlementQuoteDto>($"api/fine-settlements/{id}/quote");
    public Task<Result<IReadOnlyList<FineSettlementHistoryDto>>> GetFineSettlementHistoryAsync(Guid id) => GetAsync<IReadOnlyList<FineSettlementHistoryDto>>($"api/fine-settlements/{id}/history");
    public Task<Result<EcfCollectionDraftDto>> GetCurrentFineDraftAsync(Guid id) => GetAsync<EcfCollectionDraftDto>($"api/fine-settlements/{id}/current-draft");
    public Task<Result<EcfCollectionDraftDto>> AddFineDraftAsync(AddFineAssessmentDraftRequest request) => PostAsync<AddFineAssessmentDraftRequest,EcfCollectionDraftDto>("api/fine-settlements/drafts",request);
    public Task<Result<EcfCollectionDraftDto>> ReviewFineDraftAsync(Guid id, EcfDraftRevisionRequest request) => PostAsync<EcfDraftRevisionRequest,EcfCollectionDraftDto>($"api/fine-settlements/drafts/{id}/review",request);
    public Task<Result<EcfCollectionDraftDto>> DiscardFineDraftAsync(Guid id, EcfDraftRevisionRequest request) => PostAsync<EcfDraftRevisionRequest,EcfCollectionDraftDto>($"api/fine-settlements/drafts/{id}/discard",request);
    public Task<Result<EcfPostOutcomeDto>> PostFineDraftAsync(Guid id, PostEcfCollectionDraftRequest request) => PostAsync<PostEcfCollectionDraftRequest,EcfPostOutcomeDto>($"api/fine-settlements/drafts/{id}/post",request);
    public Task<Result<IReadOnlyList<FineAssessmentDto>>> GetAssessmentsAsync(FineAccountKind? kind = null, Guid? accountId = null, string? search = null) =>
        GetAsync<IReadOnlyList<FineAssessmentDto>>($"api/fine-assessments?kind={kind}&accountId={accountId}&search={Uri.EscapeDataString(search ?? "")}");
    public Task<Result<IReadOnlyList<FineAccountOption>>> SearchFineAccountsAsync(string search) =>
        GetAsync<IReadOnlyList<FineAccountOption>>($"api/fine-assessments/accounts?search={Uri.EscapeDataString(search)}");
    public Task<Result<FineAccountOption>> GetFineAccountAsync(FineAccountRef account) =>
        GetAsync<FineAccountOption>($"api/fine-assessments/accounts/{account.Kind}/{account.AccountId}");
    public Task<Result<IReadOnlyList<FineAccountOption>>> GetFacilityFineAccountsAsync(Guid stallId, EEMOCantilanSDS.Domain.Enums.FacilityCode facility) =>
        GetAsync<IReadOnlyList<FineAccountOption>>($"api/fine-assessments/facility-accounts?stallId={stallId}&facility={facility}");
    public Task<Result<IReadOnlyList<FinePeriodOption>>> GetFinePeriodsAsync(FineAccountRef account, int? year = null) =>
        GetAsync<IReadOnlyList<FinePeriodOption>>($"api/fine-assessments/accounts/{account.Kind}/{account.AccountId}/periods?year={year}");
    public Task<Result<IReadOnlyList<FineSourceOption>>> GetFineSourcesAsync() => GetAsync<IReadOnlyList<FineSourceOption>>("api/fine-assessments/sources");
    public Task<Result<FineAccountOption>> ResolveFacilityFineAccountAsync(Guid stallId, EEMOCantilanSDS.Domain.Enums.FacilityCode facility, DateOnly snapshotDate, Guid? contractId = null) =>
        GetAsync<FineAccountOption>($"api/fine-assessments/facility-account?stallId={stallId}&facility={facility}&snapshotDate={snapshotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&contractId={contractId}");
    public Task<Result<IReadOnlyList<FinePolicyDto>>> GetFinePoliciesAsync() => GetAsync<IReadOnlyList<FinePolicyDto>>("api/fine-assessments/policies");
    public Task<Result<FinePolicyDto>> RecordFinePolicyAsync(FinePolicyRequest request) => PostAsync<FinePolicyRequest, FinePolicyDto>("api/fine-assessments/policies", request);
    public Task<Result<FineAssessmentDto>> CreateAssessmentAsync(CreateFineAssessmentRequest request) => PostAsync<CreateFineAssessmentRequest, FineAssessmentDto>("api/fine-assessments", request);
    public Task<Result<IReadOnlyList<FineAssessmentEventDto>>> GetAssessmentHistoryAsync(Guid id) => GetAsync<IReadOnlyList<FineAssessmentEventDto>>($"api/fine-assessments/{id}/history");
    public Task<Result<IReadOnlyList<PenaltyDefinitionDto>>> GetDefinitionsAsync() =>
        GetAsync<IReadOnlyList<PenaltyDefinitionDto>>("api/penalties/definitions");

    public Task<Result<PenaltyDefinitionDto>> DefineAsync(DefinePenaltyRequest request) =>
        PostAsync<DefinePenaltyRequest, PenaltyDefinitionDto>("api/penalties/definitions", request);

    public Task<Result<IReadOnlyList<PenaltyRegisterRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<PenaltyRegisterRowDto>>(
            $"api/penalties/register?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
}
