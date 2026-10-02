using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IWcfCollectionsApiClient
{
    Task<Result<IReadOnlyList<WcfObligationQuoteDto>>> GetObligationsAsync(int throughYear, int throughMonth);
    Task<Result<IReadOnlyList<CashTicketDocumentDto>>> GetAvailableCashTicketsAsync();

    /// <summary>Head/Admin: the Water sources that can be set up for a billing period (GET api/wcf-collections/setup-sources).</summary>
    Task<Result<IReadOnlyList<WcfSetupSourceDto>>> GetSetupSourcesAsync(int billingYear, int billingMonth);

    /// <summary>Head/Admin: establish or, before settlement, revise a direct approved Water amount (POST api/wcf-collections/obligations).</summary>
    Task<Result<WcfSetupSourceDto>> EstablishObligationAsync(WcfObligationSetupRequest request);

    /// <summary>Head/Admin: WCF Mobile collection status with system-derived readiness.</summary>
    Task<Result<WcfMobileStatusDto>> GetMobileStatusAsync();

    /// <summary>Head/Admin: enable WCF Mobile collection once (idempotent; the server re-checks readiness).</summary>
    Task<Result<WcfMobileStatusDto>> EnableMobileAsync();

    /// <summary>Head/Admin: the activation dry-run for one Water obligation, with optional attested evidence.</summary>
    Task<Result<SettlementCutoverReadinessDto>> GetActivationReadinessAsync(Guid utilityBillId, SettlementCutoverReconciliationEvidence? evidence);

    /// <summary>Head/Admin: make one Water obligation collectible on Collector Mobile through the scoped cutover.</summary>
    Task<Result<SettlementCutoverOutcomeDto>> ActivateAsync(EEMOCantilanSDS.Application.Common.Revenue.WcfActivationRequest request);
    Task<Result<WcfCollectionOutcomeDto>> PostAsync(WcfCollectionPostRequest request);
    Task<Result<IReadOnlyList<WcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to);
    Task<Result<IReadOnlyList<WcfReconciliationExceptionDto>>> GetReconciliationExceptionsAsync();
    Task<Result<IReadOnlyList<AccountableFormBookDto>>> GetBooksAsync();
    Task<Result<AccountableFormBookDto>> ReceiveBookAsync(ReceiveAccountableFormBookRequest request);
    Task<Result<int>> AssignCashTicketsAsync(AssignAccountableFormRangeRequest request);

    /// <summary>Custody assignment of a received Official Receipt range (POST api/accountable-forms/official-receipts/assign).</summary>
    Task<Result<int>> AssignOfficialReceiptsAsync(AssignAccountableFormRangeRequest request);

    /// <summary>All-or-nothing assignment of several collectors' ranges from one book (POST api/accountable-forms/assign-batch).</summary>
    Task<Result<int>> AssignBatchAsync(AssignAccountableFormBatchRequest request);

    /// <summary>Moves assigned, unused units between collectors with a reason (POST api/accountable-forms/transfer).</summary>
    Task<Result<int>> TransferAsync(TransferAccountableFormsRequest request);

    /// <summary>Previews or commits an automatic allocation of in-office units across collectors (custody only).</summary>
    Task<Result<AutoAllocatePlanDto>> AutoAllocateAsync(AutoAllocateFormsRequest request);
}
