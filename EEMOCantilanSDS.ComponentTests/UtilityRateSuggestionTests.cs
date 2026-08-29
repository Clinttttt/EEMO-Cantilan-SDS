using EEMOCantilanSDS.Client.Services;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// What a metered rate's FIELD starts at, for an office that has never stated one.
///
/// <para>
/// The office asked for this: electricity and water opened at nought, which is a poor thing to hand a clerk, because
/// nought is also a real answer here meaning "type the amount on each bill". One peso reads as a placeholder and gets
/// corrected.
/// </para>
/// <para>
/// The reason it is a class of its own rather than three lines in a drawer is the direction that must never happen. A
/// rate nobody stated becoming a figure somebody is billed is the exact fault found when Madrid was charging Cantilan's
/// per-kilo fee. So these tests hold two things: that the suggestion reaches only the two metered rates and only where
/// the office has stated nothing, and that it never touches a rate the office has already stated.
/// </para>
/// </summary>
public class UtilityRateSuggestionTests
{
    [Theory]
    [InlineData("ElecPerKwh")]
    [InlineData("WaterPerCubicMeter")]
    public void AMeteredRateNobodyHasStatedOpensAtOnePeso(string key)
    {
        Assert.Equal(1.00m, UtilityRateSuggestion.StartingValueOrNull(key, stated: 0m));
    }

    [Theory]
    [InlineData("elecperkwh")]
    [InlineData("waterpercubicmeter")]
    public void TheKeyIsReadHoweverTheApiHappensToCaseIt(string key)
    {
        Assert.NotNull(UtilityRateSuggestion.StartingValueOrNull(key, stated: 0m));
    }

    [Theory]
    [InlineData("ElecPerKwh", 12.50)]
    [InlineData("WaterPerCubicMeter", 25)]
    public void AnOfficeOwnFigureIsLeftExactlyAsItStands(string key, decimal stated)
    {
        // The whole point of the ordinance is that this figure is the office's. A suggestion over the top of it would be
        // this platform pricing a utility, which it does not do.
        Assert.Null(UtilityRateSuggestion.StartingValueOrNull(key, stated));
    }

    [Theory]
    [InlineData("NpmDailyStall")]
    [InlineData("NpmMonthlyStall")]
    [InlineData("SlaughterPerHead")]
    [InlineData("TerminalPerTrip")]
    public void NothingThatIsNotMeteredIsEverSuggestedFor(string key)
    {
        // A daily stall fee left unstated has its own answer already, and it is not a peso: the market's own rate, through
        // the one fee rule. Suggesting here would put a figure in front of the office that no ordinance supports.
        Assert.Null(UtilityRateSuggestion.StartingValueOrNull(key, stated: 0m));
        Assert.False(UtilityRateSuggestion.IsMetered(key));
    }

    [Fact]
    public void AKeyThisVersionDoesNotRecogniseIsNotGuessedAt()
    {
        Assert.False(UtilityRateSuggestion.IsMetered("SomethingAddedLater"));
        Assert.Null(UtilityRateSuggestion.StartingValueOrNull("SomethingAddedLater", stated: 0m));
        Assert.Null(UtilityRateSuggestion.StartingValueOrNull(string.Empty, stated: 0m));
    }

    [Fact]
    public void AWithdrawnRateIsTreatedAsUnstatedRatherThanAsAPriceOfNought()
    {
        // An office that clears a rate back to nought is saying "the clerk enters it per bill". Opening the field at a peso
        // next time is the same offer as the first time, not a contradiction of that.
        Assert.Equal(1.00m, UtilityRateSuggestion.StartingValueOrNull("ElecPerKwh", stated: 0m));
    }

    [Fact]
    public void TheSuggestionIsOnePesoAndSaysSoInOnePlace()
    {
        // Stated once, so the drawer's note and the field cannot disagree about what the office is being offered.
        Assert.Equal(1.00m, UtilityRateSuggestion.StartingRate);
    }
}
