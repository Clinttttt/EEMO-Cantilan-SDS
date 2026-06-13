using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Staff (admin/head) reconciliation of online payments: list those awaiting an OR and encode it.</summary>
public interface IOnlinePaymentsApiClient
{
    Task<Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>> GetAwaitingOrAsync();
    Task<Result<bool>> IssueOrNumberAsync(Guid transactionId, string orNumber);
}
