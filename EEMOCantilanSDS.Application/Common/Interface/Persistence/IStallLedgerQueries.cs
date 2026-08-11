using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// What one stall has paid and what it still owes — the reading side of a stall's ledger.
///
/// <para>
/// Split out of <see cref="IPaymentRepository"/>, which had grown to fifteen members covering aggregate persistence,
/// receipt-number availability across every module, facility-wide projections and these per-stall read models at once.
/// A handler that only wants to show a stall's history had to take a dependency that can also write payments and rule on
/// OR numbers, and a test for it had to stub the lot.
/// </para>
///
/// <para>
/// Named for the business view it provides rather than for a table, which is the point: these four answer one question
/// the office asks — what does this account look like — and they answer it with projections, never with entities to
/// modify. There is deliberately no generic base repository behind them.
/// </para>
///
/// <para>
/// The implementation still lives in PaymentRepository for now, because these reads share private obligation arithmetic
/// with the rest of it; moving the code is a mechanical follow-up. Segregating the CONTRACT first is what stops new
/// callers reaching for the wide one, and it makes that move a file operation rather than a redesign.
/// </para>
/// </summary>
public interface IStallLedgerQueries
{
    /// <summary>The stall's billing months and what was collected against each.</summary>
    Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct);

    /// <summary>
    /// Cursor-paginated transparency log of a stall's collections, newest first. NPM → recorded daily
    /// collections (paid/absent); monthly facilities → payment records. Cursor is the last row's date.
    /// </summary>
    Task<CursorPagedResult<StallCollectionHistoryRowDto>> GetStallCollectionHistoryAsync(
        Guid stallId, DateTime? cursor, int pageSize, CancellationToken ct);

    /// <summary>The stall's totals: collected, outstanding, and the periods behind them.</summary>
    Task<StallLedgerSummaryDto> GetStallLedgerSummaryAsync(Guid stallId, CancellationToken ct);

    /// <summary>
    /// The unpaid billing months of one occupancy, with its own balances. Whose is decided by
    /// <paramref name="contractId"/>, else by the term that held the stall during <paramref name="forPeriod"/> (so a
    /// screen showing a past period is answered by the lessee of that period), else by the most recent term.
    /// </summary>
    Task<IReadOnlyList<PaymentHistoryDto>> GetOutstandingMonthsAsync(
        Guid stallId, Guid? contractId, DateOnly? forPeriod, CancellationToken ct);
}
