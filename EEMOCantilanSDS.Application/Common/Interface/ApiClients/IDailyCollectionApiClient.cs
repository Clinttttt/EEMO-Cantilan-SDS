using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IDailyCollectionApiClient
{
    Task<Result<long>> ActivateNpmOpeningMonthAsync(NpmOpeningMonthActivationRequest request) =>
        Task.FromResult(Result<long>.Failure("Opening-month activation is unavailable."));
    Task<Result<NpmOpeningMonthAssessmentReview>> ReviewNpmOpeningMonthAsync(Guid stallId, Guid contractId, DateOnly period) =>
        Task.FromResult(Result<NpmOpeningMonthAssessmentReview>.Failure("Prospective opening assessment is unavailable."));
    Task<Result<Guid>> PrepareNpmOpeningMonthAsync(NpmOpeningMonthPreparationRequest request) =>
        Task.FromResult(Result<Guid>.Failure("Prospective opening assessment is unavailable."));
    Task<Result<NpmRentMonthHeadReview>> ReviewNpmMonthAsync(Guid stallId, DateOnly period) => Task.FromResult(Result<NpmRentMonthHeadReview>.Failure("Month review is unavailable."));
    Task<Result<NpmRentMonthCreditOutcome>> ApplyNpmMonthCreditAsync(NpmRentMonthCreditRequest request) => Task.FromResult(Result<NpmRentMonthCreditOutcome>.Failure("Month credit review is unavailable."));
    Task<Result<NpmRentMonthPaymentOutcome>> ReverseNpmMonthAsync(NpmRentMonthReverseRequest request) => Task.FromResult(Result<NpmRentMonthPaymentOutcome>.Failure("Month correction is unavailable."));
    Task<Result<NpmRentMonthPaymentOutcome>> ReplaceNpmMonthAsync(NpmRentMonthReplacementRequest request) => Task.FromResult(Result<NpmRentMonthPaymentOutcome>.Failure("Month replacement is unavailable."));
    Task<Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthDayReview>> QuoteNpmMonthlyDayAsync(Guid stallId, DateOnly installmentDate) =>
        Task.FromResult(Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthDayReview>.Failure("The monthly day quote is unavailable."));
    Task<Result<bool>> IsNpmMonthlySettlementAvailableAsync() => Task.FromResult(Result<bool>.Success(false));
    Task<Result<EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentQuoteDto>> QuoteNpmMonthlyWholeAsync(Guid stallId, int year, int month) =>
        Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentQuoteDto>.Failure("Monthly settlement is unavailable."));
    Task<Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthPaymentOutcome>> RecordNpmMonthlyWholeAsync(EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentRequest request) =>
        Task.FromResult(Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthPaymentOutcome>.Failure("Monthly settlement is unavailable."));
    Task<Result<bool>> RecordDailyCollectionAsync(RecordDailyCollectionCommand command);
    Task<Result<DailyCollectionMonthDto>> GetDailyCollectionMonthAsync(Guid stallId, int year, int month);
    Task<Result<bool>> SaveOrNumberAsync(SaveDailyCollectionOrNumberCommand command);
    Task<Result<bool>> SaveOrForDaysAsync(SaveDailyCollectionOrForDaysCommand command);
    Task<Result<bool>> SettleNpmMonthAsync(SettleNpmMonthCommand command);
    Task<Result<bool>> SettleNpmDaysAsync(SettleNpmDaysCommand command);
    /// <summary>
    /// A month's still-settleable days. <paramref name="contractId"/> names whose days on a stall that has been
    /// re-let; omitted for the current occupancy.
    /// </summary>
    Task<Result<IReadOnlyList<SettleableNpmDayDto>>> GetSettleableNpmDaysAsync(Guid stallId, int year, int month, Guid? contractId = null);

    /// <summary>Previews an amount received against a stall's payable days of a month. Records nothing.</summary>
    Task<Result<NpmAmountQuoteDto>> QuoteNpmAmountAsync(Guid stallId, int year, int month, decimal amount) =>
        Task.FromResult(Result<NpmAmountQuoteDto>.Failure("The amount preview is unavailable."));

    /// <summary>Previews an amount received as full or part installments, oldest first. Records nothing.</summary>
    Task<Result<NpmPartPaymentQuoteDto>> QuoteNpmPartPaymentAsync(Guid stallId, int year, int month, decimal amount) =>
        Task.FromResult(Result<NpmPartPaymentQuoteDto>.Failure("The part-payment preview is unavailable."));

    /// <summary>Records a reviewed part payment as one canonical Collection.</summary>
    Task<Result<NpmPartPaymentOutcomeDto>> RecordNpmPartPaymentAsync(NpmPartPaymentRequest request) =>
        Task.FromResult(Result<NpmPartPaymentOutcomeDto>.Failure("Part payments are unavailable."));
}
