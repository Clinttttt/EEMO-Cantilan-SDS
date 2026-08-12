using EEMOCantilanSDS.Application.Dtos.Stalls;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// The register of INACTIVE stall accounts: explicitly closed (frozen) stalls and expired ones — an active stall whose
/// contract term has lapsed.
///
/// <para>
/// A read about accounts the office has stopped billing but has not stopped collecting on. An expired contract stops
/// accruing rent and keeps its balance collectable, which is exactly why this register exists as its own question: it is
/// what the follow-up queue works from and what the financial report reconciles against. It is not the stall repository's
/// business of letting, transferring or closing a stall.
/// </para>
///
/// <para>
/// The two readings are deliberately separate and must not be substituted for one another. The lifetime one answers "what
/// is owed in total"; the period one answers what that ended occupancy owed and paid FOR the period, omitting an occupancy
/// that did not exist in it. Serving a period report from the lifetime figures is how a monthly report starts showing
/// arrears that predate the month.
/// </para>
/// </summary>
public interface IClosedStallAccountQueries
{
    /// <summary>
    /// Inactive stall accounts for the register, with lifetime collected (all money ever received) and uncollected arrears
    /// accrued up to the end point (close date / contract expiry), excused/absent-aware.
    /// </summary>
    Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsAsync(CancellationToken ct);

    /// <summary>
    /// The same register bounded to a period: each figure is what that ended occupancy owed and paid FOR
    /// [<paramref name="from"/>, <paramref name="to"/>], and an occupancy that did not exist in the period is omitted.
    /// </summary>
    Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsForPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
