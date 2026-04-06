using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IPaymentsApiClient
{
    Task<Result<PaymentRecordDto>> GetPaymentRecordAsync(Guid stallId, int year, int month);
    Task<Result<IReadOnlyList<PaymentHistoryDto>>> GetPaymentHistoryAsync(Guid stallId);
    Task<Result<bool>> RecordPaymentAsync(RecordPaymentCommand command);
    Task<Result<bool>> SaveOrNumberAsync(SaveOrNumberCommand command);
}
