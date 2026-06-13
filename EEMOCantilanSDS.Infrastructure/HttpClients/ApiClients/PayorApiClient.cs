using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class PayorApiClient(HttpClient http) : HandleResponse(http), IPayorApiClient
{
    public async Task<Result<IReadOnlyList<PayorStallBalanceDto>>> GetBalancesAsync() =>
        await GetAsync<IReadOnlyList<PayorStallBalanceDto>>("api/payor/balances");

    public async Task<Result<IReadOnlyList<PaymentHistoryDto>>> GetHistoryAsync(Guid stallId) =>
        await GetAsync<IReadOnlyList<PaymentHistoryDto>>($"api/payor/stalls/{stallId}/history");

    public async Task<Result<IReadOnlyList<PayorPayableItemDto>>> GetPayableItemsAsync() =>
        await GetAsync<IReadOnlyList<PayorPayableItemDto>>("api/payor/payable-items");

    public async Task<Result<InitiateOnlinePaymentResultDto>> InitiatePaymentAsync(InitiateOnlinePaymentCommand command) =>
        await PostAsync<InitiateOnlinePaymentCommand, InitiateOnlinePaymentResultDto>("api/onlinepayments/initiate", command);
}
