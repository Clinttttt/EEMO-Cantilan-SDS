using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IWcfCollectionsApiClient
{
    Task<Result<IReadOnlyList<WcfObligationQuoteDto>>> GetObligationsAsync(int throughYear, int throughMonth);
    Task<Result<IReadOnlyList<CashTicketDocumentDto>>> GetAvailableCashTicketsAsync();
    Task<Result<WcfCollectionOutcomeDto>> PostAsync(WcfCollectionPostRequest request);
    Task<Result<IReadOnlyList<WcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to);
    Task<Result<IReadOnlyList<WcfReconciliationExceptionDto>>> GetReconciliationExceptionsAsync();
    Task<Result<IReadOnlyList<AccountableFormBookDto>>> GetBooksAsync();
    Task<Result<AccountableFormBookDto>> ReceiveBookAsync(ReceiveAccountableFormBookRequest request);
    Task<Result<int>> AssignCashTicketsAsync(AssignAccountableFormRangeRequest request);

    /// <summary>Custody assignment of a received Official Receipt range (POST api/accountable-forms/official-receipts/assign).</summary>
    Task<Result<int>> AssignOfficialReceiptsAsync(AssignAccountableFormRangeRequest request);
}
