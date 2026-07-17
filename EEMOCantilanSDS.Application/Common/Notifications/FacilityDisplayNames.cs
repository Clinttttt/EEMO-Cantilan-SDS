using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Notifications;

/// <summary>
/// Canonical, human-friendly facility names for use in notification copy. Deliberately independent of the
/// per-tenant <c>Facility.Name</c> (which may be customised) so notification text stays stable and needs no
/// extra DB lookup. Falls back to the enum name for any code without a canonical label.
/// </summary>
public static class FacilityDisplayNames
{
    public static string Of(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "New Public Market",
        FacilityCode.TCC => "Tampak Commercial Center",
        FacilityCode.NCC => "New Commercial Center",
        FacilityCode.BBQ => "Barbecue Stand",
        FacilityCode.ICE => "Iceplant",
        FacilityCode.SLH => "Slaughterhouse",
        FacilityCode.TRM => "Transport Terminal",
        FacilityCode.TPM => "Tabo-an Public Market",
        _ => code.ToString()
    };
}
