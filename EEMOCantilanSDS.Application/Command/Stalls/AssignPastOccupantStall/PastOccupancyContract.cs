using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;

/// <summary>
/// Decides which term a placement is for. A stall that has been re-let carries several terms, so naming the stall
/// alone is not enough: the latest term belongs to the lessee sitting there now, and reading it would place the
/// wrong person. The register knows which term each of its rows is the record of and passes that id.
/// </summary>
internal static class PastOccupancyContract
{
    /// <param name="stall">The stall, loaded with its contracts.</param>
    /// <param name="contractId">
    /// The term to act on. When absent — a stall with a single history, or an older caller — the most recent term is
    /// used, which is then the only occupancy there has been.
    /// </param>
    public static Contract? Resolve(Stall stall, Guid? contractId)
    {
        if (contractId is { } id && id != Guid.Empty)
            return stall.Contracts.FirstOrDefault(c => c.Id == id);

        return stall.Contracts
            .OrderByDescending(c => c.EffectivityDate)
            .ThenByDescending(c => c.CreatedAt)
            .FirstOrDefault();
    }
}
