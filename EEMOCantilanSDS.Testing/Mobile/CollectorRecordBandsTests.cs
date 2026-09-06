using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// How the mobile Records feed bands a day's entries by area.
/// </summary>
/// <remarks>
/// The feed was a flat run of cards each repeating its own area, and the office reads this market by area — the Menu's collection
/// screen already groups that way. Banded here, the card stops repeating it.
///
/// <para>The ORDER is the part worth testing. Grouping alone would fall into alphabetical order, filing Fish above Vegetable and
/// reading as though the market had been reorganised.</para>
/// </remarks>
public class CollectorRecordBandsTests
{
    private static readonly DateTime At = new(2026, 9, 4, 9, 30, 0);

    private static CollectorRecordEntry Entry(
        string payor,
        MarketSection? section = null,
        string? customSection = null,
        FacilityCode facility = FacilityCode.NPM) =>
        new(new MobileCollectorRecordDto(
            "OR-1", payor, facility, string.Empty, "1", "Daily Fee", 30m, 30m, false, At,
            section, null, false, false, null, null, customSection, new DateOnly(2026, 9, 4)));

    /// <summary>Canonical areas keep the market's own order, not the alphabet's.</summary>
    [Fact]
    public void CanonicalAreasAreBandedInTheMarketsOwnOrder()
    {
        var bands = CollectorRecordBands.Build(
        [
            Entry("Meat Payor", MarketSection.MeatSection),
            Entry("Fish Payor", MarketSection.FishSection),
            Entry("Veg Payor", MarketSection.VegetableArea),
        ]);

        Assert.Equal(["Vegetable", "Fish", "Meat"], bands.Select(b => b.Label).ToArray());
    }

    /// <summary>
    /// An area the office named itself follows the canonical ones, and keeps its own name.
    /// </summary>
    /// <remarks>
    /// The tenant's own wording, not ours: "Sari Sari" is what the office called it, and the band is the one place it is now
    /// stated. Ranked after the canonical areas so a new area does not push the market's own three out of order.
    /// </remarks>
    [Fact]
    public void AnAreaTheOfficeNamedItselfFollowsTheCanonicalOnes()
    {
        var bands = CollectorRecordBands.Build(
        [
            Entry("Sari Payor", customSection: "Sari Sari"),
            Entry("Kahoy Payor", customSection: "Kahoy Sale"),
            Entry("Veg Payor", MarketSection.VegetableArea),
        ]);

        Assert.Equal(["Vegetable", "Kahoy Sale", "Sari Sari"], bands.Select(b => b.Label).ToArray());
    }

    /// <summary>
    /// A facility with no areas is banded by the facility, and its cards then stop repeating that too.
    /// </summary>
    /// <remarks>
    /// A band with nothing to say would be worse than none: every card would sit under a heading the office cannot act on.
    /// </remarks>
    [Fact]
    public void AFacilityWithNoAreasIsBandedByTheFacility()
    {
        var slaughter = Entry("Abattoir Customer", facility: FacilityCode.SLH);
        var bands = CollectorRecordBands.Build([slaughter, Entry("Veg Payor", MarketSection.VegetableArea)]);

        // The area bands come first; the facility band is a fallback and sits last.
        Assert.Equal(["Vegetable", "SLH"], bands.Select(b => b.Label).ToArray());

        Assert.True(CollectorRecordBands.NamesTheFacility(slaughter));
        Assert.False(CollectorRecordBands.NamesTheFacility(Entry("Veg Payor", MarketSection.VegetableArea)));
        Assert.False(CollectorRecordBands.NamesTheFacility(Entry("Sari Payor", customSection: "Sari Sari")));
    }

    /// <summary>Every entry survives the banding — a card must never be lost to a grouping change.</summary>
    [Fact]
    public void NoEntryIsLostInTheBanding()
    {
        CollectorRecordEntry[] rows =
        [
            Entry("A", MarketSection.VegetableArea),
            Entry("B", MarketSection.VegetableArea),
            Entry("C", MarketSection.FishSection),
            Entry("D", customSection: "Sari Sari"),
            Entry("E", facility: FacilityCode.SLH),
        ];

        var bands = CollectorRecordBands.Build(rows);

        Assert.Equal(rows.Length, bands.Sum(b => b.Rows.Count));
        Assert.Equal(
            rows.Select(r => r.Primary.PayorName).OrderBy(n => n),
            bands.SelectMany(b => b.Rows).Select(r => r.Primary.PayorName).OrderBy(n => n));
    }
}
