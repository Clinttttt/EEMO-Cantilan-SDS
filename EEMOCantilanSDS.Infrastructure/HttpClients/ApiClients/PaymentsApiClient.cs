using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class PaymentsApiClient(HttpClient http) : HandleResponse(http), IPaymentsApiClient
{
    public async Task<Result<PaymentRecordDto>> GetPaymentRecordAsync(Guid stallId, int year, int month) =>
        await GetAsync<PaymentRecordDto>($"api/Payments/stall/{stallId}?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<FacilityPaymentRecordDto>>> GetFacilityPaymentRecordsAsync(FacilityCode facilityCode, int year, int month) =>
        await GetAsync<IReadOnlyList<FacilityPaymentRecordDto>>($"api/Payments/facility/{facilityCode}?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<PaymentHistoryDto>>> GetPaymentHistoryAsync(Guid stallId) =>
        await GetAsync<IReadOnlyList<PaymentHistoryDto>>($"api/Stalls/{stallId}/payment-history");

    public async Task<Result<bool>> RecordPaymentAsync(RecordPaymentCommand command) =>
        await PostAsync<RecordPaymentCommand, bool>("api/Payments/record", command);

    public async Task<Result<bool>> SaveOrNumberAsync(SaveOrNumberCommand command) =>
        await PostAsync<SaveOrNumberCommand, bool>("api/Payments/or-number", command);
}
