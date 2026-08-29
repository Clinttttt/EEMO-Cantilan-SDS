using EEMOCantilanSDS.Client.Services;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// A daily fee worked out from the monthly rent a clerk typed: ₱900 a month is ₱30 a day.
///
/// <para>
/// The office asked for this, and the arithmetic is its own: a daily-collected space is LET for a monthly rent, and the
/// daily fee is how that rent is collected, thirty installments to a month. The platform already runs it the other way
/// round to show a monthly equivalent on a stall.
/// </para>
/// <para>
/// The reason it is a class with tests is the direction that costs money. A figure in a custom section stall's daily field
/// becomes that stall's OWN rate, and an own rate outranks its section's for ever - the office would state a section fee
/// and go on collecting the old figure from every stall in it. That fault was found and fixed once already, and the form
/// leaves the field BLANK wherever the section carries a stated fee. So the first test here is the one that matters: a
/// blank field stays blank.
/// </para>
/// </summary>
public class DailyFeeFromMonthlyRentTests
{
    private const decimal MarketRate = 30m;      // what the form suggests, and therefore what may be replaced

    [Fact]
    public void ABlankDailyFieldStaysBlankBecauseTheStallFollowsItsSection()
    {
        // THE test. A blank field is blank on purpose: this stall is billed its section's stated fee. A figure here would
        // outrank that fee for ever, which is exactly the fault this platform has already had once.
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(monthlyRent: 900m, dailyNow: 0m, dailyOnOpen: MarketRate, lastDerived: null));
    }

    [Fact]
    public void ABlankFieldStaysBlankEvenWhenNothingElseWouldStopIt()
    {
        // The same rule, isolated. Above, the field differs from what the form opened with, so the "clerk's own figure"
        // rule would refuse anyway and the blank rule is never reached. Here the field is blank AND unchanged AND matches
        // the last answer, so only the blank rule stands between a priced section and a stall that outranks it.
        //
        // Written after an injection proof: deleting the blank rule left every other test passing.
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(900m, dailyNow: 0m, dailyOnOpen: 0m, lastDerived: null));
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(900m, dailyNow: 0m, dailyOnOpen: 0m, lastDerived: 0m));
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(900m, dailyNow: 0m, dailyOnOpen: 30m, lastDerived: 0m));
    }

    [Fact]
    public void NineHundredAMonthIsThirtyADay()
    {
        Assert.Equal(30m, DailyFeeFromMonthlyRent.DerivedOrNull(900m, MarketRate, MarketRate, null));
    }

    [Theory]
    [InlineData(1200, 40)]
    [InlineData(1500, 50)]
    [InlineData(600, 20)]
    public void AnyRentDividesTheSameWay(decimal rent, decimal daily)
    {
        Assert.Equal(daily, DailyFeeFromMonthlyRent.DerivedOrNull(rent, MarketRate, MarketRate, null));
    }

    [Fact]
    public void ARentThatDoesNotDivideCleanlyIsKeptToCentavos()
    {
        // ₱1,000 a month is ₱33.33 a day. Two decimals, because that is what a rate field holds and what a bill states.
        Assert.Equal(33.33m, DailyFeeFromMonthlyRent.DerivedOrNull(1000m, MarketRate, MarketRate, null));
    }

    [Fact]
    public void TheClerksOwnFigureIsNeverOverwritten()
    {
        // The clerk typed 45. Correcting the rent afterwards must not quietly undo that.
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(900m, dailyNow: 45m, dailyOnOpen: MarketRate, lastDerived: null));
    }

    [Fact]
    public void ItsOwnPreviousAnswerIsReplacedAsTheRentIsCorrected()
    {
        // A clerk typing 9-0-0 fires this on every keystroke, and again when they fix a typo. Its own answers are its own
        // to replace; only the clerk's are not.
        var first = DailyFeeFromMonthlyRent.DerivedOrNull(900m, MarketRate, MarketRate, null);
        Assert.Equal(30m, first);

        var corrected = DailyFeeFromMonthlyRent.DerivedOrNull(1200m, dailyNow: first!.Value, dailyOnOpen: MarketRate, lastDerived: first);
        Assert.Equal(40m, corrected);
    }

    [Fact]
    public void NoRentYetLeavesTheFieldAloneRatherThanClearingIt()
    {
        // Mid-typing, and a cleared field. Dropping the daily fee to nought here would read as "free" on the form that
        // records the stall.
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(0m, MarketRate, MarketRate, null));
        Assert.Null(DailyFeeFromMonthlyRent.DerivedOrNull(-100m, MarketRate, MarketRate, null));
    }

    [Fact]
    public void ARentSoSmallItWouldPriceAStallAtNothingIsStillStatedAsTheOfficeTypedIt()
    {
        // ₱15 a month is 50 centavos a day. Absurd, and not this class's business to refuse: the form validates, and an
        // office that types 15 needs to see what it means rather than have it silently ignored.
        Assert.Equal(0.50m, DailyFeeFromMonthlyRent.DerivedOrNull(15m, MarketRate, MarketRate, null));
    }
}
