using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class DailyCollectionApiClient(HttpClient http) : HandleResponse(http), IDailyCollectionApiClient
{
    public Task<Result<long>> ActivateNpmOpeningMonthAsync(NpmOpeningMonthActivationRequest request) =>
        PostAsync<NpmOpeningMonthActivationRequest, long>("api/DailyCollections/npm-month/activate-opening", request);
    public Task<Result<NpmOpeningMonthAssessmentReview>> ReviewNpmOpeningMonthAsync(Guid stallId, Guid contractId, DateOnly period) =>
        GetAsync<NpmOpeningMonthAssessmentReview>($"api/DailyCollections/npm-month/opening-assessment?stallId={stallId}&contractId={contractId}&period={period:yyyy-MM-dd}");
    public Task<Result<Guid>> PrepareNpmOpeningMonthAsync(NpmOpeningMonthPreparationRequest request) =>
        PostAsync<NpmOpeningMonthPreparationRequest, Guid>("api/DailyCollections/npm-month/prepare-opening", request);
    public Task<Result<NpmRentMonthHeadReview>> ReviewNpmMonthAsync(Guid stallId, DateOnly period) => GetAsync<NpmRentMonthHeadReview>($"api/DailyCollections/npm-month/review?stallId={stallId}&period={period:yyyy-MM-dd}");
    public Task<Result<NpmRentMonthCreditOutcome>> ApplyNpmMonthCreditAsync(NpmRentMonthCreditRequest request) => PostAsync<NpmRentMonthCreditRequest, NpmRentMonthCreditOutcome>("api/DailyCollections/npm-month/credits", request);
    public Task<Result<NpmRentMonthPaymentOutcome>> ReverseNpmMonthAsync(NpmRentMonthReverseRequest request) => PostAsync<NpmRentMonthReverseRequest, NpmRentMonthPaymentOutcome>("api/DailyCollections/npm-month/reverse", request);
    public Task<Result<NpmRentMonthPaymentOutcome>> ReplaceNpmMonthAsync(NpmRentMonthReplacementRequest request) => PostAsync<NpmRentMonthReplacementRequest, NpmRentMonthPaymentOutcome>("api/DailyCollections/npm-month/replace", request);
    public Task<Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthDayReview>> QuoteNpmMonthlyDayAsync(Guid stallId, DateOnly installmentDate) =>
        GetAsync<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthDayReview>($"api/DailyCollections/npm-month/day-quote?stallId={stallId}&installmentDate={installmentDate:yyyy-MM-dd}");
    public Task<Result<bool>> IsNpmMonthlySettlementAvailableAsync() => GetAsync<bool>("api/DailyCollections/npm-month/available");
    public Task<Result<EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentQuoteDto>> QuoteNpmMonthlyWholeAsync(Guid stallId, int year, int month) =>
        GetAsync<EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentQuoteDto>($"api/DailyCollections/npm-month/whole-quote?stallId={stallId}&year={year}&month={month}");
    public Task<Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthPaymentOutcome>> RecordNpmMonthlyWholeAsync(EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentRequest request) =>
        PostAsync<EEMOCantilanSDS.Application.Dtos.Mobile.NpmWholePaymentRequest, EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthPaymentOutcome>("api/DailyCollections/npm-month/whole-payment", request);
    public async Task<Result<bool>> RecordDailyCollectionAsync(RecordDailyCollectionCommand command) =>
        await PostAsync<RecordDailyCollectionCommand, bool>("api/DailyCollections/record", command);

    public async Task<Result<DailyCollectionMonthDto>> GetDailyCollectionMonthAsync(Guid stallId, int year, int month) =>
        await GetAsync<DailyCollectionMonthDto>($"api/DailyCollections/stall/{stallId}/month?year={year}&month={month}");

    public async Task<Result<bool>> SaveOrNumberAsync(SaveDailyCollectionOrNumberCommand command) =>
        await PostAsync<SaveDailyCollectionOrNumberCommand, bool>("api/DailyCollections/or", command);

    public async Task<Result<bool>> SaveOrForDaysAsync(SaveDailyCollectionOrForDaysCommand command) =>
        await PostAsync<SaveDailyCollectionOrForDaysCommand, bool>("api/DailyCollections/or-days", command);

    public async Task<Result<bool>> SettleNpmMonthAsync(SettleNpmMonthCommand command) =>
        await PostAsync<SettleNpmMonthCommand, bool>("api/DailyCollections/settle-month", command);

    public async Task<Result<bool>> SettleNpmDaysAsync(SettleNpmDaysCommand command) =>
        await PostAsync<SettleNpmDaysCommand, bool>("api/DailyCollections/settle-days", command);

    public async Task<Result<IReadOnlyList<SettleableNpmDayDto>>> GetSettleableNpmDaysAsync(
        Guid stallId, int year, int month, Guid? contractId = null)
    {
        var query = $"api/DailyCollections/stall/{stallId}/settleable-days?year={year}&month={month}";
        if (contractId is { } id && id != Guid.Empty)
            query += $"&contractId={id}";

        return await GetAsync<IReadOnlyList<SettleableNpmDayDto>>(query);
    }

    public async Task<Result<NpmAmountQuoteDto>> QuoteNpmAmountAsync(Guid stallId, int year, int month, decimal amount) =>
        await GetAsync<NpmAmountQuoteDto>($"api/DailyCollections/stall/{stallId}/amount-quote?year={year}&month={month}&amount={amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public async Task<Result<NpmPartPaymentQuoteDto>> QuoteNpmPartPaymentAsync(Guid stallId, int year, int month, decimal amount) =>
        await GetAsync<NpmPartPaymentQuoteDto>($"api/npm-daily/stall/{stallId}/part-payment-quote?year={year}&month={month}&amount={amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public async Task<Result<NpmPartPaymentOutcomeDto>> RecordNpmPartPaymentAsync(NpmPartPaymentRequest request) =>
        await PostAsync<NpmPartPaymentRequest, NpmPartPaymentOutcomeDto>("api/npm-daily/part-payment", request);
}
