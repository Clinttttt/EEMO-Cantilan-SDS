using System.Linq.Expressions;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Common;

/// <summary>Which utility a service relationship is for. Settled independently: neither payment settles the other.</summary>
public enum UtilityKind { Electricity = 1, Water = 2 }

/// <summary>
/// The one rule for whether a market space is a utility source (IA-053 / IA-063). ECF and WCF are MEEDO utility operations,
/// not parts of NPM rent, so a space of ANY facility is eligible when its own utility-service flag says it is metered or
/// serviced for that utility. The New Public Market keeps the behaviour it has always had: every occupied NPM stall is a source
/// (its flags were never the gate there), so nothing already collectable is withdrawn.
///
/// <para>A name, an occupant, a vendor registration or a facility alone never makes a source: only the space's own flag
/// (or the NPM's standing rule) does. Sources that are not market spaces (an independent vendor registration, an event lot)
/// carry no utility-service record yet and are therefore NOT eligible here; offering them needs an explicit, Head-authorized,
/// effective-dated service association that does not exist.</para>
/// </summary>
public static class UtilityServiceEligibility
{
    public static ApplicableFees FlagFor(UtilityKind kind) => kind == UtilityKind.Electricity ? ApplicableFees.Electricity : ApplicableFees.Water;

    /// <summary>True when the stall (with its facility loaded) is a source for this utility.</summary>
    public static bool IsSource(Stall stall, UtilityKind kind) =>
        stall.Facility?.Code == FacilityCode.NPM || (stall.Fees & FlagFor(kind)) != 0;

    /// <summary>The same rule as a query predicate, so the database filters and pages by it.</summary>
    public static Expression<Func<Stall, bool>> SourcePredicate(UtilityKind kind)
    {
        var flag = FlagFor(kind);
        return stall => stall.Facility!.Code == FacilityCode.NPM || (stall.Fees & flag) != 0;
    }
}
