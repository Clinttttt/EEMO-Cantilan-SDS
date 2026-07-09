using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Constants
{
    /// <summary>
    /// Canonical presentation defaults for the 8 standard facility types. Used when offering a facility a
    /// tenant has not configured yet (the "available to add" list) and as the seed defaults when one is
    /// added. Rates and billing behaviour remain per-LGU data (<c>FacilityRate</c> / <c>BillingArchetype</c>);
    /// this only supplies a sensible name/short-name so the UI never shows a bare enum code.
    /// </summary>
    public static class FacilityCatalog
    {
        public static readonly IReadOnlyList<FacilityCode> AllCodes = new[]
        {
            FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ,
            FacilityCode.ICE, FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM,
        };

        public static (string Name, string ShortName) Defaults(FacilityCode code) => code switch
        {
            FacilityCode.NPM => ("New Public Market", "NPM"),
            FacilityCode.TCC => ("Tampak Commercial Center", "TCC"),
            FacilityCode.NCC => ("New Commercial Center", "NCC"),
            FacilityCode.BBQ => ("Barbecue Stand", "BBQ"),
            FacilityCode.ICE => ("Iceplant", "ICE"),
            FacilityCode.SLH => ("Slaughterhouse", "SLH"),
            FacilityCode.TRM => ("Transport Terminal", "TRM"),
            FacilityCode.TPM => ("Tabo-an Public Market", "TPM"),
            _ => (code.ToString(), code.ToString()),
        };
    }
}
