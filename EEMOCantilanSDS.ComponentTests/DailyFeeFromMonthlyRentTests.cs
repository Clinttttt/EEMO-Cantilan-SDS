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
    public void ARentThatDoesNotDivideCleanlyIsRoundedToTheNearestPeso()
    {
        // ₱800 a month is ₱26.67 a day exactly, and a collector cannot make change for 67 centavos at a stall. Every fee
        // this platform bills daily is a whole peso, and an ordinance schedule is written that way.
        Assert.Equal(27m, DailyFeeFromMonthlyRent.DerivedOrNull(800m, MarketRate, MarketRate, null));

        // ₱1,000 a month is ₱33.33 a day, which rounds down.
        Assert.Equal(33m, DailyFeeFromMonthlyRent.DerivedOrNull(1000m, MarketRate, MarketRate, null));
    }

    [Fact]
    public void RoundingIsToTheNEARESTPesoRatherThanDownwards()
    {
        // Nearest, so the installment stays as close as a whole peso can be to a thirtieth of the rent. Always rounding
        // down would make every derived fee too small by up to 99 centavos a day, and a custom-section stall's month is
        // thirty of these, so the office would collect up to ₱29.70 less a month than its own figure implies.
        Assert.Equal(27m, DailyFeeFromMonthlyRent.DerivedOrNull(805m, MarketRate, MarketRate, null));   // 26.83
        Assert.Equal(27m, DailyFeeFromMonthlyRent.DerivedOrNull(795m, MarketRate, MarketRate, null));   // 26.50, up
        Assert.Equal(26m, DailyFeeFromMonthlyRent.DerivedOrNull(780m, MarketRate, MarketRate, null));   // 26.00 exactly
    }

    [Fact]
    public void ANeverExactRentStillLeavesAFigureAClerkCanCollect()
    {
        // No centavos reach the field at all, whatever is typed above it.
        foreach (var rent in new[] { 1m, 7m, 99m, 1001m, 12345m })
        {
            var derived = DailyFeeFromMonthlyRent.DerivedOrNull(rent, MarketRate, MarketRate, null);
            Assert.NotNull(derived);
            Assert.Equal(decimal.Truncate(derived!.Value), derived.Value);
        }
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
    public void ARentSoSmallItWouldPriceAStallAtAlmostNothingIsStillAnswered()
    {
        // ₱15 a month is 50 centavos a day, which rounds to a peso. Absurd, and not this class's business to refuse: the
        // form validates, and an office that types 15 needs to see what it means rather than have it silently ignored.
        Assert.Equal(1m, DailyFeeFromMonthlyRent.DerivedOrNull(15m, MarketRate, MarketRate, null));
    }

    // ── The other direction: a daily fee implies a monthly rent ─────────────────────────────────────────────────────
    //
    // Needed rather than convenient. On the rent-goal basis the server REFUSES a market stall with no monthly rate, and the form
    // opened a custom-section stall with its daily rate filled and the monthly at nought - so the office completed the form and
    // was refused with nothing on screen to say which figure was wanted.

    /// <summary>Thirty of the stall's own daily fee, which is exactly what Stall.ResolveMonthlyRent bills it.</summary>
    [Fact]
    public void ThirtyPesosADayIsNineHundredAMonth()
    {
        Assert.Equal(900m, DailyFeeFromMonthlyRent.MonthlyFromDailyOrNull(30m, monthlyNow: 0m, lastDerived: null));
    }

    /// <summary>The office's own figure stands: a recorded rent is the contract's record, not something a rate edit may move.</summary>
    [Fact]
    public void ARentAlreadyRecordedIsNotOverwritten()
    {
        Assert.Null(DailyFeeFromMonthlyRent.MonthlyFromDailyOrNull(30m, monthlyNow: 850m, lastDerived: null));
    }

    /// <summary>But its own previous answer may be revised, or a corrected daily rate would leave a stale rent behind it.</summary>
    [Fact]
    public void ItsOwnPreviousAnswerIsRevised()
    {
        Assert.Equal(810m, DailyFeeFromMonthlyRent.MonthlyFromDailyOrNull(27m, monthlyNow: 900m, lastDerived: 900m));
    }

    /// <summary>Nothing to work from leaves the field alone rather than writing a nought over it.</summary>
    [Fact]
    public void NoDailyFeeMeansNoSuggestion()
    {
        Assert.Null(DailyFeeFromMonthlyRent.MonthlyFromDailyOrNull(0m, monthlyNow: 0m, lastDerived: null));
    }

    /// <summary>
    /// The two directions agree at the figures that matter.
    /// </summary>
    /// <remarks>
    /// ₱27 a day is the case that proves it: the office types ₱800 a month, the daily rounds to ₱27, and the month that stall
    /// actually owes is thirty of those - ₱810. Both directions state that, so the form cannot show one figure and bill another.
    /// </remarks>
    [Fact]
    public void TheTwoDirectionsAgree()
    {
        var daily = DailyFeeFromMonthlyRent.DerivedOrNull(800m, MarketRate, MarketRate, null);
        Assert.Equal(27m, daily);
        Assert.Equal(810m, DailyFeeFromMonthlyRent.MonthlyFromDailyOrNull(daily!.Value, 0m, null));
    }
}