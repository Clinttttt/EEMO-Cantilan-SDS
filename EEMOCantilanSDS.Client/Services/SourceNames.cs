using System.Globalization;
using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Presentation names for revenue sources. Stable semantic codes (PERMANENT_STALL_RENT, TRANSPORTATION_PARKING, …) stay the
/// persisted identity; screens show a professional name. A tenant's own policy display name wins when it is a real name;
/// a name that is only the code (or an acronym standing in for one) is replaced by the office wording used across V3.
/// </summary>
public static partial class SourceNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["PERMANENT_STALL_RENT"] = "Permanent Stall Rent",
        ["ECF"] = "Electricity (ECF)",
        ["WCF"] = "Water (WCF)",
        ["FISH_MEAT_VENDOR_FEE"] = "Fish / Meat Vendor Fee",
        ["WEIGHT_AND_MEASURE"] = "Weight & Measure",
        ["PENALTIES_AND_FINES"] = "Penalties / Fines",
        ["SLAUGHTERHOUSE"] = "Slaughterhouse",
        ["MARKET_FEES"] = "Market Fees",
        ["TABO"] = "Tabo",
        ["TRANSPORTATION_PARKING"] = "Transportation / Parking",
        ["TRANSPORTATION"] = "Transportation / Parking",
        ["VEGETABLE_FRUIT_SPACE_RENTAL"] = "Vegetable / Fruit Space Rental",
        ["LANDING_BERTHING"] = "Landing / Berthing",
        ["ARREARS"] = "Arrears",
        ["TRANSFER_LARGE_CATTLE"] = "Transfer Large Cattle",
        ["ICE_PLANT"] = "Ice Plant",
        ["KANMANGGAY_SPACE_RENTAL"] = "Kanmanggay Space Rental",
        ["FIESTA_ARAW_LOT_RENTAL"] = "Fiesta / Araw Lot Rental",
        ["UNKNOWN_CLASSIFICATION"] = "Unclassified",
    };

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex RawCode();

    /// <summary>True when the text looks like a stable code rather than a name.</summary>
    public static bool LooksLikeCode(string? text) =>
        !string.IsNullOrWhiteSpace(text) && RawCode().IsMatch(text.Trim()) && (text.Contains('_') || Names.ContainsKey(text.Trim()));

    /// <summary>The professional name for a source code, preferring a real configured name.</summary>
    public static string For(string? code, string? configuredName = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredName) && !LooksLikeCode(configuredName) && configuredName.Trim() != code)
            return configuredName.Trim();
        if (string.IsNullOrWhiteSpace(code)) return configuredName?.Trim() ?? string.Empty;
        return Names.TryGetValue(code.Trim(), out var name) ? name : Humanize(code);
    }

    /// <summary>A name to show for a value that is usually a name but may be a stable code (an unnamed policy, a test tenant).</summary>
    public static string Name(string? nameOrCode) =>
        string.IsNullOrWhiteSpace(nameOrCode) ? string.Empty
        : LooksLikeCode(nameOrCode) ? For(nameOrCode.Trim()) : nameOrCode.Trim();

    /// <summary>A readable name for an unmapped code: words title-cased, underscores as spaces.</summary>
    public static string Humanize(string code)
    {
        var words = code.Trim().ToLowerInvariant().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words);
    }
}
