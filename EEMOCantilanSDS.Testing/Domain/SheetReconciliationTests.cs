using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Checking an office sheet's own figures against what the term can support.
///
/// <para>
/// The import collected a Delinquent (₱) and a Whole Year Rental from every row and discarded both. Neither can be
/// stored — whole-year rental is derived, and there is no opening-balance concept, so a stated arrears figure would be
/// added on top of the arrears the system already computes and over-state the account. They are used as a check
/// instead, which is the reconciliation the office does by hand.
/// </para>
/// </summary>
public class SheetReconciliationTests
{
    private static readonly DateOnly Jun2023 = new(2023, 6, 7);

    [Fact]
    public void AThreeYearTermCanBillExactlyThirtySixMonths()
    {
        // The N × 12 rule, which is where this check earns its keep: the term above touches 37 calendar months, and
        // billing both part-Junes is what produced ₱33,300 where the office's sheet said ₱32,400.
        var ceiling = SheetReconciliation.MaximumBillableToDate(Jun2023, 3, 900m, new DateOnly(2026, 8, 8));

        Assert.Equal(36 * 900m, ceiling);
        Assert.Equal(32_400m, ceiling);
        Assert.NotEqual(33_300m, ceiling);
    }

    [Fact]
    public void AStatedFigureAboveWhatTheTermCouldEverBill_IsFlagged()
    {
        // ₱33,300 is 37 months of a 36-month term. However much or little was paid, the term cannot have billed it.
        Assert.True(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(
            33_300m, Jun2023, 3, 900m, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void AStatedFigureTheTermCouldBill_IsNotFlagged()
    {
        var asOf = new DateOnly(2026, 8, 8);
        Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(32_400m, Jun2023, 3, 900m, asOf));
        Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(900m, Jun2023, 3, 900m, asOf));
    }

    [Fact]
    public void ALowerFigureIsNeverFlagged_BecauseThatIsWhatAPartPaidAccountLooksLike()
    {
        // An import carries no payment history. Flagging a low figure would accuse every payor who has paid something,
        // and an office that is warned wrongly stops reading warnings.
        var asOf = new DateOnly(2026, 8, 8);
        foreach (var stated in new[] { 1m, 450m, 10_000m, 32_399m })
            Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(stated, Jun2023, 3, 900m, asOf));
    }

    [Fact]
    public void ATermStillRunningIsBoundedByTheMonthsThatHaveOpened_NotByTheWholeTerm()
    {
        // Six months into a three-year term: at most six months can have been billed, not thirty-six.
        var start = new DateOnly(2026, 3, 1);
        var ceiling = SheetReconciliation.MaximumBillableToDate(start, 3, 1_000m, new DateOnly(2026, 8, 8));

        Assert.Equal(6 * 1_000m, ceiling);
        Assert.True(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(
            7_000m, start, 3, 1_000m, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void ATermThatHasNotStartedBillsNothing()
    {
        var start = new DateOnly(2026, 12, 1);
        Assert.Equal(0m, SheetReconciliation.MaximumBillableToDate(start, 3, 1_000m, new DateOnly(2026, 8, 8)));
        // With no ceiling to compare against, nothing is asserted rather than flagging the row.
        Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(
            5_000m, start, 3, 1_000m, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void AnOpenEndedSpaceIsNotChecked()
    {
        // A space let without a contract has no term to bound it, so no claim can be made about its arrears.
        Assert.Equal(0m, SheetReconciliation.MaximumBillableToDate(
            Jun2023, DomainRules.OpenEndedTermYears, 1_500m, new DateOnly(2026, 8, 8)));
        Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(
            999_999m, Jun2023, DomainRules.OpenEndedTermYears, 1_500m, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void NoRateOnRecordMeansNoClaimIsMade()
    {
        Assert.Equal(0m, SheetReconciliation.MaximumBillableToDate(Jun2023, 3, 0m, new DateOnly(2026, 8, 8)));
        Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(
            5_000m, Jun2023, 3, 0m, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void AWholeYearFigureThatIsNotTwelveMonthsRent_IsFlagged()
    {
        Assert.True(SheetReconciliation.WholeYearDisagreesWithMonthly(10_000m, 900m));   // should be 10,800
        Assert.True(SheetReconciliation.WholeYearDisagreesWithMonthly(33_120m, 2_400m)); // 2,760's total on 2,400's row
        Assert.False(SheetReconciliation.WholeYearDisagreesWithMonthly(10_800m, 900m));
        Assert.False(SheetReconciliation.WholeYearDisagreesWithMonthly(28_800m, 2_400m));
    }

    [Fact]
    public void AnAbsentFigureIsNotAnError()
    {
        // The office leaves these blank on most rows; a blank must never raise a warning.
        Assert.False(SheetReconciliation.ArrearsExceedsWhatTermCouldBill(0m, Jun2023, 3, 900m, new DateOnly(2026, 8, 8)));
        Assert.False(SheetReconciliation.WholeYearDisagreesWithMonthly(0m, 900m));
        Assert.False(SheetReconciliation.WholeYearDisagreesWithMonthly(10_800m, 0m));
    }
}
