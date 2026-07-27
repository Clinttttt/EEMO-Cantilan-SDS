using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.FacilityArt;

/// <summary>Ink a facility mark is drawn in — chosen by the surface it sits on.</summary>
public enum FacilityMarkInk
{
    /// <summary>Navy, for light surfaces (white cards, light tiles).</summary>
    Navy,

    /// <summary>White, for dark surfaces (the navy sidebar).</summary>
    White,

    /// <summary>Gold, for the navy facility hero, where gold is the established accent.</summary>
    Gold
}

/// <summary>
/// The line artwork for each facility, drawn for legibility at NAVIGATION size (16–24 px).
///
/// Why hand-authored paths rather than the office's illustrations: the illustrations carry far too much
/// detail to survive being scaled to a sidebar row — at 18 px they collapsed into a smudge. Each icon here
/// is a handful of strokes on a 24×24 grid, was checked by rendering it at 16/18/24/30 px on both a navy and
/// a light background, and inherits its colour from a stroke value (so there are no per-ink raster variants
/// and no image files to ship).
///
/// Conventions: 24×24 viewBox, 1.8 stroke, round caps and joins, no fills, no text, no currency symbols.
/// Facilities of the same kind share a drawing — the two commercial centres are one icon, and Tabo-an uses
/// the public-market stall because it is the same kind of facility, collected weekly.
///
/// This file is COMPILED BY BOTH presentation projects (the portal and the collector app) through a linked
/// Compile item in the Mobile csproj, so the two can never show different artwork. It deliberately depends on
/// nothing beyond the FacilityCode enum.
/// </summary>
public static class FacilityMarkArt
{
    /// <summary>
    /// Inner SVG markup for the facility, or null when it has none (a Head-added custom facility), letting
    /// the caller fall back to its own generic glyph.
    /// </summary>
    public static string? PathsFor(FacilityCode code) => code switch
    {
        // Public market — a market stall: canopy wider than the frame, two posts, counter.
        FacilityCode.NPM or FacilityCode.TPM =>
            """<path d="M3 9.5 5.5 4.5h13L21 9.5Z"/><path d="M6 9.5V19"/><path d="M18 9.5V19"/><path d="M6 13.5h12"/>""",

        // Commercial centres — one storefront block: windows and a door (shared by both by design).
        FacilityCode.TCC or FacilityCode.NCC =>
            """<rect x="4" y="6" width="16" height="14" rx="1.5"/><path d="M8 10h2"/><path d="M14 10h2"/><path d="M8 14h2"/><path d="M14 14h2"/><path d="M10.5 20v-3h3v3"/>""",

        // Barbecue stand — kettle grill on splayed legs. The two heat wisps are what keep it from reading as
        // the market stall's counter at 16 px.
        FacilityCode.BBQ =>
            """<path d="M4 9.5h16"/><path d="M5.5 9.5c0 4.2 2.9 6.8 6.5 6.8s6.5-2.6 6.5-6.8"/><path d="m9.5 16-2.5 4.5"/><path d="m14.5 16 2.5 4.5"/><path d="M9.5 4c-.9 1.1.9 1.9 0 3"/><path d="M14.5 4c-.9 1.1.9 1.9 0 3"/>""",

        // Iceplant — a snowflake: cold storage, and never mistakable for a currency symbol.
        FacilityCode.ICE =>
            """<path d="M12 3v18"/><path d="m4.2 7.5 15.6 9"/><path d="m19.8 7.5-15.6 9"/><path d="m9.6 5.4 2.4-2.4 2.4 2.4"/><path d="m9.6 18.6 2.4 2.4 2.4-2.4"/>""",

        // Slaughterhouse — a livestock head (horns carry the shape at small sizes). Chosen over a cleaver,
        // which read as a video camera at 16 px, and it suits an official document better.
        FacilityCode.SLH =>
            """<path d="M5 5.5c1.9 0 3.1 1.1 3.5 2.5"/><path d="M19 5.5c-1.9 0-3.1 1.1-3.5 2.5"/><path d="M7.5 8h9a2 2 0 0 1 2 2v1.5a6.5 6.5 0 0 1-13 0V10a2 2 0 0 1 2-2Z"/><path d="M10.5 14h3"/>""",

        // Transport terminal — a bus: body, window band, two wheels.
        FacilityCode.TRM =>
            """<rect x="3.5" y="5" width="17" height="11" rx="2"/><path d="M3.5 10.5h17"/><circle cx="7.5" cy="18.5" r="1.6"/><circle cx="16.5" cy="18.5" r="1.6"/>""",

        _ => null
    };

    /// <summary>True when this facility has dedicated artwork.</summary>
    public static bool Has(FacilityCode code) => PathsFor(code) is not null;

    /// <summary>Stroke colour for an ink. Literal values so the collector app needs no CSS variables.</summary>
    public static string StrokeFor(FacilityMarkInk ink) => ink switch
    {
        FacilityMarkInk.White => "#ffffff",
        FacilityMarkInk.Gold => "#c8a84b",
        _ => "#0d2137"
    };
}
