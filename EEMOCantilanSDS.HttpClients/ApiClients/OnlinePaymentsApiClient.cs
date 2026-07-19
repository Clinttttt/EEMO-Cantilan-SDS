using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class OnlinePaymentsApiClient(HttpClient http) : HandleResponse(http), IOnlinePaymentsApiClient
{
    public async Task<Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>> GetAwaitingOrAsync() =>
        await GetAsync<IReadOnlyList<OnlinePaymentAwaitingOrDto>>("api/onlinepayments/awaiting-or");

    public async Task<Result<bool>> IssueOrNumberAsync(Guid transactionId, string orNumber) =>
        await PostAsync<object, bool>($"api/onlinepayments/{transactionId}/or-number", new { ORNumber = orNumber });

    public async Task<Result<OnlinePaymentDashboardDto>> GetDashboardAsync() =>
        await GetAsync<OnlinePaymentDashboardDto>("api/onlinepayments/dashboard");
}
