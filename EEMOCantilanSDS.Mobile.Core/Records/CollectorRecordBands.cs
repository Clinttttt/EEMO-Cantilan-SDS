using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.Records;

/// <summary>
/// How the mobile Records feed bands a day's entries by the area they were collected in.
///
/// <para>
/// Asked for 2026-09-06. The feed was a flat run of cards, each repeating its own area, and the office reads this market by area
/// - the Menu's collection screen already groups that way, so this is the same idea in the place the collector checks its work
/// rather than a new pattern. With the area stated once at the head of its band, the card no longer repeats it.
/// </para>
///
/// <para>
/// This lives outside the razor page for the same reason the grouping does: the ORDER is a rule, not a detail. Canonical areas
/// keep the market's own order rather than falling into alphabetical order, which would file Fish before Vegetable and read as
/// though the market had been reorganised. The mobile UI itself has no automated coverage, so a rule left in the page is a rule
/// nothing checks.
/// </para>
/// </summary>
public static class CollectorRecordBands
{
    // Ranks, not indices: canonical areas sort by the market's own enum order, then areas the office named itself, then a
    // facility band. Spaced apart so a new canonical section cannot collide with the fallbacks.
    private const int CustomAreaRank = 1_000;
    private const int FacilityRank = 2_000;

    /// <summary>A day's entries, banded and in the order the office reads them.</summary>
    public static IReadOnlyList<CollectorRecordBand> Build(IEnumerable<CollectorRecordEntry> rows) =>
        rows
            .GroupBy(LabelOf)
            .OrderBy(g => RankOf(g.First()))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CollectorRecordBand(g.Key, g.ToList()))
            .ToList();

    /// <summary>
    /// What the band is called.
    /// </summary>
    /// <remarks>
    /// The canonical area name for a canonical section, the office's OWN name for an area it added itself, and the facility
    /// where a facility has no areas at all - because a band with nothing to say would be worse than none, leaving every card
    /// under a heading the office cannot act on.
    /// </remarks>
    public static string LabelOf(CollectorRecordEntry row)
    {
        var record = row.Primary;

        if (record.Section is { } section) return SectionLabel(section);
        if (!string.IsNullOrWhiteSpace(record.CustomSectionName)) return record.CustomSectionName!.Trim();

        return record.FacilityCode.ToString();
    }

    /// <summary>True where the band already names the facility, so the card need not repeat it.</summary>
    public static bool NamesTheFacility(CollectorRecordEntry row) =>
        row.Primary.Section is null && string.IsNullOrWhiteSpace(row.Primary.CustomSectionName);

    /// <summary>The market's own area names, as the collector's cards have always shown them.</summary>
    public static string SectionLabel(MarketSection section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable",
        MarketSection.FishSection => "Fish",
        MarketSection.MeatSection => "Meat",
        _ => section.ToString(),
    };

    private static int RankOf(CollectorRecordEntry row)
    {
        var record = row.Primary;

        if (record.Section is { } section) return (int)section;
        if (!string.IsNullOrWhiteSpace(record.CustomSectionName)) return CustomAreaRank;

        return FacilityRank;
    }
}

/// <summary>One area band on the Records feed, and the entries collected in it.</summary>
public sealed record CollectorRecordBand(string Label, IReadOnlyList<CollectorRecordEntry> Rows);
