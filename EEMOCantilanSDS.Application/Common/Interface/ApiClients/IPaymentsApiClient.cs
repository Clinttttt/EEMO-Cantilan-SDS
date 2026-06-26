using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;
using EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;
using EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IPaymentsApiClient
{
    Task<Result<PaymentRecordDto>> GetPaymentRecordAsync(Guid stallId, int year, int month);
    Task<Result<IReadOnlyList<FacilityPaymentRecordDto>>> GetFacilityPaymentRecordsAsync(FacilityCode facilityCode, int year, int month);
    Task<Result<IReadOnlyList<NpmStallDailyStatusDto>>> GetNpmDailyStatusAsync(FacilityCode facilityCode, int year, int month);
    Task<Result<IReadOnlyList<PaymentHistoryDto>>> GetPaymentHistoryAsync(Guid stallId);
    Task<Result<StallLedgerSummaryDto>> GetStallLedgerSummaryAsync(Guid stallId);
    Task<Result<bool>> RecordPaymentAsync(RecordPaymentCommand command);
    Task<Result<bool>> SaveOrNumberAsync(SaveOrNumberCommand command);

    Task<Result<IReadOnlyList<int>>> GetMonthlyExceptionsAsync(Guid stallId, int year);
    Task<Result<bool>> SetMonthlyExceptionAsync(SetStallMonthlyExceptionCommand command);
    Task<Result<bool>> ClearMonthlyExceptionAsync(ClearStallMonthlyExceptionCommand command);
    Task<Result<IReadOnlyList<int>>> GetMarketClosuresAsync(int year, int month);
    Task<Result<bool>> SetMarketClosureAsync(SetNpmMarketClosureCommand command);
    Task<Result<bool>> ClearMarketClosureAsync(ClearNpmMarketClosureCommand command);
}
