using EEMOCantilanSDS.Application.Dtos.Tenancy;

namespace EEMOCantilanSDS.Mobile.Presentation;

/// <summary>
/// Which municipality an UNBOUND device signs in to. A device with a confirmed binding always uses its bound code and never
/// reaches this. Otherwise: the single live LGU, or — when several are live — the one explicitly marked default (Cantilan
/// for the golden deployment). Never the first row or the first alphabetically: if no live LGU, or more than one, is marked
/// default, the answer is null and the server refuses the unscoped login ("select your municipality") instead of guessing.
/// </summary>
public static class LoginTenant
{
    public static string? ResolveUnbound(IEnumerable<MunicipalityDto> municipalities)
    {
        var active = municipalities.Where(m => m.IsActive).ToList();
        if (active.Count == 1) return active[0].Code;

        var defaults = active.Where(m => m.IsDefault).ToList();
        return defaults.Count == 1 ? defaults[0].Code : null;
    }
}
