using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class OnlinePaymentsApiClient(HttpClient http) : HandleResponse(http), IOnlinePaymentsApiClient
{
    public async Task<Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>> GetAwaitingOrAsync() =>
        await GetAsync<IReadOnlyList<OnlinePaymentAwaitingOrDto>>("api/onlinepayments/awaiting-or");

    public async Task<Result<bool>> IssueOrNumberAsync(Guid transactionId, string orNumber) =>
        await PostAsync<object, bool>($"api/onlinepayments/{transactionId}/or-number", new { ORNumber = orNumber });

    public async Task<Result<OnlinePaymentDashboardDto>> GetDashboardAsync() =>
        await GetAsync<OnlinePaymentDashboardDto>("api/onlinepayments/dashboard");

    public async Task<Result<IReadOnlyList<OnlinePaymentExceptionDto>>> GetExceptionsAsync() =>
        await GetAsync<IReadOnlyList<OnlinePaymentExceptionDto>>("api/onlinepayments/exceptions");

    public async Task<Result<ConfirmOnlinePaymentResultDto>> VerifyAsync(string reference) =>
        await PostAsync<object, ConfirmOnlinePaymentResultDto>($"api/onlinepayments/{Uri.EscapeDataString(reference)}/reconcile", new { });

    public async Task<Result<bool>> ResolveExceptionAsync(Guid transactionId, OnlinePaymentDisposition disposition, string reference, string? note) =>
        await PostAsync<object, bool>($"api/onlinepayments/{transactionId}/resolution", new { Disposition = disposition, Reference = reference, Note = note });
}
