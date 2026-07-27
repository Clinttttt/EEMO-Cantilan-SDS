using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Utilities;

/// <summary>Which ink a facility mark is drawn in — chosen by the surface it sits on.</summary>
public enum FacilityMarkInk
{
    /// <summary>Navy artwork, for light surfaces (white cards, panels, tables).</summary>
    Navy,

    /// <summary>White artwork, for dark surfaces (the navy sidebar, facility hero bands).</summary>
    White
}

/// <summary>
/// The single mapping from a facility to its official mark, so the sidebar, the facility pages and the
/// dashboard can never drift apart.
///
/// The images live in <c>wwwroot/images/facility-marks</c> and are generated from the office's source
/// artwork by <c>tools/generate-facility-marks.ps1</c> (cropped, resized, and produced in both inks — the
/// source is white line art, which would be invisible on a white card).
///
/// A facility with no dedicated mark — a Head-added custom facility — returns null so the caller can fall
/// back to its generic glyph. Nothing here is per-municipality: the artwork depicts the KIND of facility,
/// while every visible label still comes from the tenant's own facility name.
/// </summary>
public static class FacilityMarks
{
    private static readonly Dictionary<FacilityCode, string> Slugs = new()
    {
        [FacilityCode.NPM] = "npm",
        [FacilityCode.TCC] = "tcc",
        [FacilityCode.NCC] = "ncc",
        [FacilityCode.BBQ] = "bbq",
        [FacilityCode.ICE] = "ice",
        [FacilityCode.SLH] = "slh",
        [FacilityCode.TRM] = "trm",
        // Tabo-an is a public market collected weekly, so it shares the public-market mark by design.
        [FacilityCode.TPM] = "tpm"
    };

    /// <summary>True when this facility has a dedicated mark.</summary>
    public static bool Has(FacilityCode code) => Slugs.ContainsKey(code);

    /// <summary>
    /// The web path of the mark, or null when the facility has none (caller should render its own glyph).
    /// </summary>
    public static string? PathFor(FacilityCode code, FacilityMarkInk ink) =>
        Slugs.TryGetValue(code, out var slug)
            ? $"/images/facility-marks/{slug}-{(ink == FacilityMarkInk.White ? "white" : "navy")}.png"
            : null;
}
