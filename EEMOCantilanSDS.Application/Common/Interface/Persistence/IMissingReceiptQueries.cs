using EEMOCantilanSDS.Application.Dtos.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Historical cash/field records with an unfilled optional OR-number field.
///
/// <para>
/// Distinct from money a stall owes: these are already-paid source records with unfilled optional
/// receipt-number evidence. They belong in operational/historical inspection, not an urgent debt queue.

/// </para>
///
/// <para>
/// Split out of <see cref="IPaymentRepository"/> so the follow-up reports stop depending on a contract that can also
/// write payments and rule on OR availability. Online payments are excluded from both: they surface through their own
/// awaiting-OR queue, and mixing the two would have the office chasing a receipt the gateway is about to issue.
/// </para>
/// </summary>
public interface IMissingReceiptQueries
{
    /// <summary>
    /// Fully-paid cash/field records whose optional OR-number field is blank for the period.
    /// Returns monthly records (one per stall) and NPM daily collections (grouped per stall).
    /// Online payments are excluded; they surface via the online awaiting-OR queue.
    /// </summary>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsAsync(int year, int month, CancellationToken ct);

    /// <summary>
    /// Whole-year variant: one row per (stall, billing month) for every blank-OR paid cash/field record in
    /// the year. Powers the Follow-up History "Whole year" Missing-OR aggregation.
    /// </summary>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsForYearAsync(int year, CancellationToken ct);

    /// <summary>
    /// All-time variant: every blank-OR paid cash/field record, whatever period it belongs to, each row carrying its own
    /// year and month. Powers the Follow-up History "Whole time" view.
    /// </summary>
    /// <remarks>
    /// A blank OR is not a period figure — the record has no receipt and will not acquire one because the calendar turned —
    /// so a cumulative view is exactly where an old one must still be findable.
    /// </remarks>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsAllTimeAsync(CancellationToken ct);
}
