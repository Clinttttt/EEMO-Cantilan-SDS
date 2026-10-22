using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.SaveSlaughterOrNumber;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.SetSlaughterAnimalLabels;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ISlaughterApiClient
{
    Task<Result<SlaughterOverviewDto>> GetOverviewAsync(int year, int month);
    Task<Result<SlaughterHistoryDto>> GetHistoryAsync(int year);
    Task<Result<IReadOnlyList<SlaughterTransactionDto>>> GetTransactionsAsync(int year, int month);
    Task<Result<IReadOnlyList<OwnerTransactionGroupDto>>> GetGroupedTransactionsAsync(int year, int month);
    Task<Result<OwnerTransactionHistoryDto>> GetOwnerHistoryAsync(string ownerName, int year, int month);
    Task<Result<bool>> RecordTransactionAsync(RecordSlaughterCommand command);
    Task<Result<bool>> UpdateTransactionAsync(UpdateSlaughterCommand command);
    Task<Result<bool>> SaveOrNumberAsync(SaveSlaughterOrNumberCommand command);
    Task<Result<ClientProfileDto>> GetClientProfileAsync(string ownerName);
    Task<Result<SlaughterAnimalLabelsDto>> GetAnimalLabelsAsync();
    Task<Result<bool>> SetAnimalLabelsAsync(SetSlaughterAnimalLabelsCommand command);
    Task<Result<IReadOnlyList<SlaughterAnimalRateDto>>> GetAnimalRatesAsync(bool activeOnly);
    Task<Result<SlaughterAnimalRateDto>> SaveApprovedAnimalAsync(SaveApprovedSlaughterAnimalRequest request);
    Task<Result<SlaughterAnimalRateDto>> SetApprovedAnimalActiveAsync(Guid id, bool isActive);
    /// <summary>Office (Head/Admin) collection by approved charges: the charges offered, the server's quote, and the canonical record.</summary>
    Task<Result<SlaughterTermsDto>> GetOfficeTermsAsync(DateOnly date) => Task.FromResult(Result<SlaughterTermsDto>.Failure("Approved charges are unavailable."));
    Task<Result<FeeScheduleCollectionWorkflow.SlaughterQuote>> QuoteOfficeAsync(FeeSchedulePostRequest request) =>
        Task.FromResult(Result<FeeScheduleCollectionWorkflow.SlaughterQuote>.Failure("Approved charges are unavailable."));
    Task<Result<GovernedServiceOutcomeDto>> RecordOfficeAsync(FeeSchedulePostRequest request) =>
        Task.FromResult(Result<GovernedServiceOutcomeDto>.Failure("Approved charges are unavailable."));
}
