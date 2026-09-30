using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class WcfCollectionsApiClient(HttpClient http) : HandleResponse(http), IWcfCollectionsApiClient
{
    public Task<Result<IReadOnlyList<WcfObligationQuoteDto>>> GetObligationsAsync(int throughYear, int throughMonth) =>
        GetAsync<IReadOnlyList<WcfObligationQuoteDto>>(
            $"api/wcf-collections/obligations?throughYear={throughYear}&throughMonth={throughMonth}");

    public Task<Result<IReadOnlyList<CashTicketDocumentDto>>> GetAvailableCashTicketsAsync() =>
        GetAsync<IReadOnlyList<CashTicketDocumentDto>>("api/wcf-collections/cash-tickets/available");

    public Task<Result<WcfCollectionOutcomeDto>> PostAsync(WcfCollectionPostRequest request) =>
        PostAsync<WcfCollectionPostRequest, WcfCollectionOutcomeDto>("api/wcf-collections/collections", request);

    public Task<Result<IReadOnlyList<WcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<WcfCollectionActivityDto>>(
            $"api/wcf-collections/activity?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

    public Task<Result<IReadOnlyList<WcfReconciliationExceptionDto>>> GetReconciliationExceptionsAsync() =>
        GetAsync<IReadOnlyList<WcfReconciliationExceptionDto>>("api/wcf-collections/reconciliation");

    public Task<Result<IReadOnlyList<AccountableFormBookDto>>> GetBooksAsync() =>
        GetAsync<IReadOnlyList<AccountableFormBookDto>>("api/accountable-forms/books");

    public Task<Result<AccountableFormBookDto>> ReceiveBookAsync(ReceiveAccountableFormBookRequest request) =>
        PostAsync<ReceiveAccountableFormBookRequest, AccountableFormBookDto>("api/accountable-forms/books", request);

    public Task<Result<int>> AssignCashTicketsAsync(AssignAccountableFormRangeRequest request) =>
        PostAsync<AssignAccountableFormRangeRequest, int>("api/accountable-forms/cash-tickets/assign", request);
}
