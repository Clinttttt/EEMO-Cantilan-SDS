using EEMOCantilanSDS.Application.Dtos.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Collections that were taken but carry no receipt number yet — the office's "awaiting OR" queue.
///
/// <para>
/// A distinct question from what a stall owes. These reads scan a whole PERIOD across every payor looking for money
/// already received whose Official Receipt has not been written down, because a payment without one is a payment the
/// office cannot account for at audit. That is a follow-up task, not a balance.
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
    /// Fully-paid records whose OR is still blank for the given period — the cash/field "awaiting OR"
    /// queue. Returns monthly records (one per stall) and NPM daily collections (grouped per stall).
    /// Online payments are excluded; they surface via the online awaiting-OR queue.
    /// </summary>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsAsync(int year, int month, CancellationToken ct);

    /// <summary>
    /// Whole-year variant: one row per (stall, billing month) for every blank-OR paid cash/field record in
    /// the year. Powers the Follow-up History "Whole year" Missing-OR aggregation.
    /// </summary>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsForYearAsync(int year, CancellationToken ct);
}
