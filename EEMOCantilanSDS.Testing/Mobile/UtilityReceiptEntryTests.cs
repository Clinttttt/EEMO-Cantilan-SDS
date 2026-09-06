using EEMOCantilanSDS.Mobile.Collections;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Which receipt number each utility is recorded under when a collector settles a stall's electricity and water.
/// </summary>
/// <remarks>
/// The sheet used to demand the number twice, once under each utility, so a payor handed ONE receipt for the whole bill made the
/// collector type the same digits again to satisfy a field. Entered once now, with separate receipts still available for offices
/// that issue them that way.
///
/// <para>The assertions that matter are the ones about a utility NOT being collected. A shared number copied onto an unpaid row
/// would put a receipt against money nobody took, on a government record, and would read back as a collection.</para>
/// </remarks>
public class UtilityReceiptEntryTests
{
    /// <summary>One receipt covering both utilities is recorded against both.</summary>
    [Fact]
    public void OneReceiptCoveringBothIsRecordedAgainstBoth()
    {
        var (elec, water) = UtilityReceiptEntry.Resolve(
            separateReceipts: false, sharedOr: "OR-2201", elecOr: null, waterOr: null,
            collectingElec: true, collectingWater: true);

        Assert.Equal("OR-2201", elec);
        Assert.Equal("OR-2201", water);
    }

    /// <summary>
    /// A utility that is not being collected carries no receipt number.
    /// </summary>
    /// <remarks>
    /// The payor settled the electricity and left the water owing. Copying the receipt onto water would show the office a
    /// receipted water bill that nobody paid.
    /// </remarks>
    [Fact]
    public void AUtilityNotBeingCollectedCarriesNoReceipt()
    {
        var (elec, water) = UtilityReceiptEntry.Resolve(
            separateReceipts: false, sharedOr: "OR-2201", elecOr: null, waterOr: null,
            collectingElec: true, collectingWater: false);

        Assert.Equal("OR-2201", elec);
        Assert.Equal(string.Empty, water);
    }

    /// <summary>
    /// The same holds the other way round: water settled, electricity left owing.
    /// </summary>
    /// <remarks>
    /// Written after an injection proof passed with the electricity side of the rule deleted — the suite only covered water being
    /// left out, so half the rule was unguarded. Both directions are stated now.
    /// </remarks>
    [Fact]
    public void TheRuleHoldsForEitherUtility()
    {
        var (elec, water) = UtilityReceiptEntry.Resolve(
            separateReceipts: false, sharedOr: "OR-2201", elecOr: null, waterOr: null,
            collectingElec: false, collectingWater: true);

        Assert.Equal(string.Empty, elec);
        Assert.Equal("OR-2201", water);
    }

    /// <summary>The same rule holds when the office issues a receipt per utility.</summary>
    [Fact]
    public void SeparateReceiptsAreKeptApart_AndAnUncollectedOneStaysEmpty()
    {
        var (elec, water) = UtilityReceiptEntry.Resolve(
            separateReceipts: true, sharedOr: "IGNORED", elecOr: "OR-11", waterOr: "OR-12",
            collectingElec: true, collectingWater: true);

        Assert.Equal("OR-11", elec);
        Assert.Equal("OR-12", water);

        var (elecOnly, waterEmpty) = UtilityReceiptEntry.Resolve(
            separateReceipts: true, sharedOr: null, elecOr: "OR-11", waterOr: "OR-12",
            collectingElec: true, collectingWater: false);

        Assert.Equal("OR-11", elecOnly);
        Assert.Equal(string.Empty, waterEmpty);
    }

    /// <summary>
    /// The shared box is ignored while separate receipts are in force, and the separate boxes while it is not.
    /// </summary>
    /// <remarks>
    /// A collector who types a number, changes their mind about the arrangement and types another must not have the abandoned one
    /// submitted quietly underneath.
    /// </remarks>
    [Fact]
    public void OnlyTheFieldsInForceAreRead()
    {
        var (sharedElec, sharedWater) = UtilityReceiptEntry.Resolve(
            separateReceipts: false, sharedOr: "OR-SHARED", elecOr: "OR-ABANDONED", waterOr: "OR-ABANDONED",
            collectingElec: true, collectingWater: true);

        Assert.Equal("OR-SHARED", sharedElec);
        Assert.Equal("OR-SHARED", sharedWater);

        var (sepElec, sepWater) = UtilityReceiptEntry.Resolve(
            separateReceipts: true, sharedOr: "OR-ABANDONED", elecOr: "OR-11", waterOr: "OR-12",
            collectingElec: true, collectingWater: true);

        Assert.Equal("OR-11", sepElec);
        Assert.Equal("OR-12", sepWater);
    }

    /// <summary>Surrounding spaces are trimmed, since a receipt number is compared and printed.</summary>
    [Fact]
    public void ReceiptNumbersAreTrimmed()
    {
        var (elec, water) = UtilityReceiptEntry.Resolve(
            separateReceipts: false, sharedOr: "  OR-2201  ", elecOr: null, waterOr: null,
            collectingElec: true, collectingWater: true);

        Assert.Equal("OR-2201", elec);
        Assert.Equal("OR-2201", water);
    }
}
