using EEMOCantilanSDS.Application.Command.OnlinePayments.Confirm;
using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Authenticated payor portal data (read-only) plus initiating an online payment. Registered
/// with the auth/refresh handlers so the payor's access token is attached automatically.</summary>
public interface IPayorApiClient
{
    Task<Result<IReadOnlyList<PayorStallBalanceDto>>> GetBalancesAsync();
    Task<Result<IReadOnlyList<PaymentHistoryDto>>> GetHistoryAsync(Guid stallId);
    Task<Result<IReadOnlyList<PayorPayableItemDto>>> GetPayableItemsAsync();
    Task<Result<InitiateOnlinePaymentResultDto>> InitiatePaymentAsync(InitiateOnlinePaymentCommand command);

    /// <summary>Reconciliation fallback after returning from hosted checkout: verifies + settles the
    /// payment for the given reference directly with the provider (webhook stays primary).</summary>
    Task<Result<ConfirmOnlinePaymentResultDto>> ConfirmPaymentAsync(string reference);
}
