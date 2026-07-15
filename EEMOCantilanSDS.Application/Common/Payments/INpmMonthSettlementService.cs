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
    /// Marks the stall's settleable days for the month Paid with a blank OR (staff encode the OR later),
    /// creating day rows as needed. Does not save — the caller's unit of work commits. Returns the days settled.
    /// When <paramref name="maxAmount"/> is given, settles days oldest-first only while their cumulative fee
    /// stays within that captured amount — so an online payment can never settle more than it paid for
    /// (e.g. a checkout that crossed midnight and exposed an extra unpaid day).
    /// </summary>
    Task<IReadOnlyList<DailyCollection>> SettleUnpaidDaysAsync(
        Stall stall, int year, int month, Guid? collectorId, string recordedBy, CancellationToken ct, decimal? maxAmount = null);
}
