using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;
using EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;
using EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class PaymentsApiClient(HttpClient http) : HandleResponse(http), IPaymentsApiClient
{
    public async Task<Result<PaymentRecordDto>> GetPaymentRecordAsync(Guid stallId, int year, int month) =>
        await GetAsync<PaymentRecordDto>($"api/Payments/stall/{stallId}?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<FacilityPaymentRecordDto>>> GetFacilityPaymentRecordsAsync(FacilityCode facilityCode, int year, int month) =>
        await GetAsync<IReadOnlyList<FacilityPaymentRecordDto>>($"api/Payments/facility/{facilityCode}?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<NpmStallDailyStatusDto>>> GetNpmDailyStatusAsync(FacilityCode facilityCode, int year, int month) =>
        await GetAsync<IReadOnlyList<NpmStallDailyStatusDto>>($"api/Payments/facility/{facilityCode}/daily-status?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<PaymentHistoryDto>>> GetPaymentHistoryAsync(Guid stallId) =>
        await GetAsync<IReadOnlyList<PaymentHistoryDto>>($"api/Stalls/{stallId}/payment-history");

    public async Task<Result<StallLedgerSummaryDto>> GetStallLedgerSummaryAsync(Guid stallId) =>
        await GetAsync<StallLedgerSummaryDto>($"api/Stalls/{stallId}/ledger-summary");

    public async Task<Result<bool>> RecordPaymentAsync(RecordPaymentCommand command) =>
        await PostAsync<RecordPaymentCommand, bool>("api/Payments/record", command);

    public async Task<Result<bool>> SaveOrNumberAsync(SaveOrNumberCommand command) =>
        await PostAsync<SaveOrNumberCommand, bool>("api/Payments/or-number", command);

    public async Task<Result<IReadOnlyList<int>>> GetMonthlyExceptionsAsync(Guid stallId, int year) =>
        await GetAsync<IReadOnlyList<int>>($"api/Payments/stall/{stallId}/monthly-exceptions?year={year}");

    public async Task<Result<bool>> SetMonthlyExceptionAsync(SetStallMonthlyExceptionCommand command) =>
        await PostAsync<SetStallMonthlyExceptionCommand, bool>("api/Payments/monthly-exception", command);

    public async Task<Result<bool>> ClearMonthlyExceptionAsync(ClearStallMonthlyExceptionCommand command) =>
        await PostAsync<ClearStallMonthlyExceptionCommand, bool>("api/Payments/monthly-exception/clear", command);

    public async Task<Result<IReadOnlyList<int>>> GetMarketClosuresAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<int>>($"api/Payments/market-closures?year={year}&month={month}");

    public async Task<Result<bool>> SetMarketClosureAsync(SetNpmMarketClosureCommand command) =>
        await PostAsync<SetNpmMarketClosureCommand, bool>("api/Payments/market-closure", command);

    public async Task<Result<bool>> ClearMarketClosureAsync(ClearNpmMarketClosureCommand command) =>
        await PostAsync<ClearNpmMarketClosureCommand, bool>("api/Payments/market-closure/clear", command);
}
