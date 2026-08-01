using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

/// <summary>
/// Who held a stall when. A stall outlives its lessees, so naming a payment's payor from the stall's CURRENT contract
/// puts a former lessee's money under the sitting lessee's name — on a receipt list, on a stall's collection history,
/// wherever the office reads back what was collected. This answers the only correct question: who was answerable for
/// that day, or for that billing month?
///
/// <para>Built from the stalls and their terms in one read, then answered in memory: the occupancy rules live on the
/// entity and cannot be expressed in SQL.</para>
/// </summary>
internal sealed class OccupantDirectory
{
    private readonly Dictionary<Guid, IReadOnlyList<StallOccupancy>> _byStall;
    private readonly DateOnly _asOf;

    private OccupantDirectory(Dictionary<Guid, IReadOnlyList<StallOccupancy>> byStall, DateOnly asOf)
    {
        _byStall = byStall;
        _asOf = asOf;
    }

    /// <param name="stalls">Stalls with their contracts loaded.</param>
    /// <param name="asOf">The reading date, used only to decide which occupancy counts as current.</param>
    public static OccupantDirectory From(IEnumerable<Stall> stalls, DateOnly asOf)
    {
        var byStall = new Dictionary<Guid, IReadOnlyList<StallOccupancy>>();
        foreach (var stall in stalls)
            byStall[stall.Id] = stall.Occupancies(asOf);

        return new OccupantDirectory(byStall, asOf);
    }

    /// <summary>
    /// The occupant answerable for a business date — the day a daily collection was taken FOR, not the day the money
    /// was handed over. Falls back to the stall's most recent occupant when no term covers the date, so a row is never
    /// left nameless.
    /// </summary>
    public string? OnDate(Guid stallId, DateOnly date)
    {
        if (!_byStall.TryGetValue(stallId, out var windows) || windows.Count == 0)
            return null;

        var holder = windows.FirstOrDefault(o => o.Start <= date && date <= o.End);
        return (holder ?? windows[^1]).Contract.ActualOccupant;
    }

    /// <summary>
    /// The occupant answerable for a billing month. A month handed over mid-way is answered for by the lessee whose
    /// occupancy started latest within it — the same rule the registers and reports use, so the two agree.
    /// </summary>
    public string? InMonth(Guid stallId, int year, int month)
    {
        if (!_byStall.TryGetValue(stallId, out var windows) || windows.Count == 0)
            return null;

        var start = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var holder = windows
            .Where(o => o.Start <= end && start <= o.End)
            .OrderByDescending(o => o.Start)
            .FirstOrDefault();

        return (holder ?? windows[^1]).Contract.ActualOccupant;
    }
}
