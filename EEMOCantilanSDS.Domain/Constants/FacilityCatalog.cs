using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Constants
{
    /// <summary>
    /// Canonical presentation defaults for the 8 standard facility types. Used when offering a facility a
    /// tenant has not configured yet (the "available to add" list) and as the seed defaults when one is
    /// added. Rates and billing behaviour remain per-LGU data (<c>FacilityRate</c> / <c>BillingArchetype</c>);
    /// this only supplies a sensible name/short-name so the UI never shows a bare enum code. The names are municipality-NEUTRAL (the
    /// codes stay the stable identity, the Head names the facility in their own words on adding it), and the two commercial centers stay
    /// distinguishable: TCC is "Commercial Center", NCC is "New Commercial Center". Nothing here renames a facility already configured.
    /// </summary>
    public static class FacilityCatalog
    {
        public static readonly IReadOnlyList<FacilityCode> AllCodes = new[]
        {
            FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ,
            FacilityCode.ICE, FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM,
        };

        /// <summary>Reserved slots for per-LGU custom (monthly-rental) facilities.</summary>
        public static readonly IReadOnlyList<FacilityCode> CustomCodes = new[]
        {
            FacilityCode.Custom1, FacilityCode.Custom2, FacilityCode.Custom3,
            FacilityCode.Custom4, FacilityCode.Custom5,
        };

        /// <summary>True for a reserved custom facility slot.</summary>
        public static bool IsCustom(FacilityCode code) =>
            code is FacilityCode.Custom1 or FacilityCode.Custom2 or FacilityCode.Custom3
                 or FacilityCode.Custom4 or FacilityCode.Custom5;

        public static (string Name, string ShortName) Defaults(FacilityCode code) => code switch
        {
            FacilityCode.NPM => ("Public Market", "NPM"),
            FacilityCode.TCC => ("Commercial Center", "TCC"),
            FacilityCode.NCC => ("New Commercial Center", "NCC"),
            FacilityCode.BBQ => ("Barbecue Stand", "BBQ"),
            FacilityCode.ICE => ("Iceplant", "ICE"),
            FacilityCode.SLH => ("Slaughterhouse", "SLH"),
            FacilityCode.TRM => ("Transport Terminal", "TRM"),
            FacilityCode.TPM => ("Weekly Market", "TPM"),
            // Custom slots have no canonical name — the Head names them; this is only a placeholder.
            _ => ("Custom Facility", "CUS"),
        };
    }
}
