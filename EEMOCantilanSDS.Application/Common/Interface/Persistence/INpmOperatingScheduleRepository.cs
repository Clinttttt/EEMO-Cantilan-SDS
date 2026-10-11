using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>What opening or closing one New Public Market day would change, before anyone confirms it.</summary>
/// <param name="BillableAccounts">NPM accounts with a term that answers for the day.</param>
/// <param name="UnpaidAccounts">Of those, accounts with nothing collected and no individual excuse — the ones a closure would excuse.</param>
/// <param name="PaidAccounts">Of those, accounts that already paid for the day. A closure never unpays them.</param>
/// <param name="UnpaidAmount">What those unpaid accounts would owe for the day at their own daily fee.</param>
public sealed record NpmDayImpact(int BillableAccounts, int UnpaidAccounts, int PaidAccounts, decimal UnpaidAmount);

/// <summary>
/// Reads and writes the New Public Market's operating calendar: the weekly schedule's versions, the explicit closure and
/// opening rows, and the day-impact preview. The effective closed dates themselves come from
/// <see cref="Domain.Common.NpmOperatingCalendar"/>, never from this contract.
/// </summary>
public interface INpmOperatingScheduleRepository
{
    /// <summary>Every schedule version, oldest first (read-only).</summary>
    Task<IReadOnlyList<NpmOperatingSchedule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>The version that takes effect on exactly this date, tracked for revision; null when none.</summary>
    Task<NpmOperatingSchedule?> GetByEffectiveDateAsync(DateOnly date, CancellationToken ct = default);

    Task AddAsync(NpmOperatingSchedule schedule, CancellationToken ct = default);

    /// <summary>Explicit closure and opening rows between two dates, both included (read-only).</summary>
    Task<IReadOnlyList<NpmMarketClosure>> GetRowsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<NpmDayImpact> GetDayImpactAsync(DateOnly date, CancellationToken ct = default);
}
