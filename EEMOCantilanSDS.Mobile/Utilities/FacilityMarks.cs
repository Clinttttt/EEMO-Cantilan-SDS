using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.Utilities;

/// <summary>Which ink a facility mark is drawn in — chosen by the surface it sits on.</summary>
public enum FacilityMarkInk
{
    /// <summary>Navy artwork, for light surfaces (the facility cards' light tiles).</summary>
    Navy,

    /// <summary>White artwork, for dark surfaces.</summary>
    White
}

/// <summary>
/// The collector app's copy of the facility-mark mapping.
///
/// Deliberately duplicated rather than shared with the portal: the two apps have separate wwwroots and this
/// project does not reference the Client. What prevents drift is the ASSET pipeline —
/// <c>tools/generate-facility-marks.ps1</c> writes both apps' folders from the same source artwork in one
/// run — plus the slugs below matching the portal's.
///
/// A facility with no dedicated mark (a Head-added custom facility) returns null so the caller falls back to
/// its generic glyph.
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

    public static string? PathFor(FacilityCode code, FacilityMarkInk ink) =>
        Slugs.TryGetValue(code, out var slug)
            ? $"images/facility-marks/{slug}-{(ink == FacilityMarkInk.White ? "white" : "navy")}.png"
            : null;
}
