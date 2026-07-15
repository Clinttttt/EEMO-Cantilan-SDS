using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Shared NPM whole-month daily-fee settlement, used by both the staff <c>SettleNpmMonth</c> command
/// and the online-payment path so the online amount and the settled days are always identical.
/// </summary>
public interface INpmMonthSettlementService
{
    /// <summary>Count + total base fee of the stall's settleable (unpaid, elapsed, in-term, non-closed) days for the month. No mutation.</summary>
    Task<NpmMonthPayable> ComputePayableAsync(Stall stall, int year, int month, CancellationToken ct);

    /// <summary>
    /// The stall's still-settleable day DATES for the month (same rule as <see cref="ComputePayableAsync"/>),
    /// for the fish-day picker so the payor can only declare/pay an uncollected, in-term, elapsed, non-closed day.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> GetPayableDaysAsync(Stall stall, int year, int month, CancellationToken ct);

    /// <summary>
    /// Marks the stall's settleable days for the month Paid with a blank OR (staff encode the OR later),
    /// creating day rows as needed. Does not save — the caller's unit of work commits. Returns the days settled.
    /// When <paramref name="maxAmount"/> is given, settles days oldest-first only while their cumulative fee
    /// stays within that captured amount — so an online payment can never settle more than it paid for
    /// (e.g. a checkout that crossed midnight and exposed an extra unpaid day).
    /// </summary>
    Task<IReadOnlyList<DailyCollection>> SettleUnpaidDaysAsync(
        Stall stall, int year, int month, Guid? collectorId, string recordedBy, CancellationToken ct, decimal? maxAmount = null);

    /// <summary>
    /// Prices ONE NPM fish-section day for online self-declaration: base daily fee + declared kilos ×
    /// the fish ₱/kg rate, both resolved as-of the day from the CURRENT municipality's fee snapshot (so
    /// custom LGUs use their own configured rates, Cantilan the ordinance constants). Returns not-payable
    /// with a reason if the day is in the future, outside the stall's contract term, a market closure, or
    /// already collected/excused. No mutation.
    /// </summary>
    Task<NpmFishDayQuote> QuoteFishDayAsync(Stall stall, DateOnly day, decimal declaredKilos, CancellationToken ct);

    /// <summary>
    /// Marks a single NPM fish day Paid with the payor-declared kilos and a blank OR (no collector — an
    /// online, payor-declared collection), creating the day row if needed and stamping the as-of base fee.
    /// A day already collected/excused in person (between checkout and settlement) is left untouched. Does
    /// not save — the caller's unit of work commits. Returns the settled/existing day.
    /// </summary>
    Task<DailyCollection?> SettleFishDayAsync(
        Stall stall, DateOnly day, decimal declaredKilos, string recordedBy, CancellationToken ct);
}
