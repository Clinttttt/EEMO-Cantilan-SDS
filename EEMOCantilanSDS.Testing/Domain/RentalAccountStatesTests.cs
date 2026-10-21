using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>The one classification of a rental account on a business date. An end date is the LAST day, so an end date that has not passed is still active.</summary>
public class RentalAccountStatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Theory]
    [InlineData(null, false, false, null, RentalAccountState.Active)]
    [InlineData("2026-11-01", false, false, null, RentalAccountState.Ending)]            // a future end date is not closed
    [InlineData("2026-10-20", false, false, null, RentalAccountState.Ending)]            // the last day is still a day of the account
    [InlineData("2026-10-19", false, false, null, RentalAccountState.Closed)]
    [InlineData("2026-06-30", false, true, null, RentalAccountState.Superseded)]         // closed and someone holds the space now
    [InlineData("2026-06-30", true, false, null, RentalAccountState.Retired)]
    [InlineData(null, false, false, "2026-10-19", RentalAccountState.TermExpired)]       // open, signed term run out: not closed
    [InlineData(null, false, false, "2026-10-20", RentalAccountState.Active)]            // the term's last day is still inside the term
    [InlineData("2026-11-01", false, false, "2026-01-01", RentalAccountState.Ending)]    // an end date outranks an old term
    public void Classifies(string? endsOn, bool retired, bool successor, string? termLast, RentalAccountState expected) =>
        Assert.Equal(expected, RentalAccountStates.Classify(endsOn is null ? null : DateOnly.Parse(endsOn), retired, successor, termLast is null ? null : DateOnly.Parse(termLast), Today));

    [Fact]
    public void OpenMeansStillCollectibleAndManageable_ClosedSupersededAndRetiredAreNot()
    {
        foreach (var open in new[] { RentalAccountState.Active, RentalAccountState.Ending, RentalAccountState.TermExpired }) Assert.True(RentalAccountStates.IsOpen(open));
        foreach (var done in new[] { RentalAccountState.Closed, RentalAccountState.Superseded, RentalAccountState.Retired }) Assert.False(RentalAccountStates.IsOpen(done));
        Assert.False(RentalAccountStates.HasEnded(new DateOnly(2026, 10, 20), Today));
        Assert.True(RentalAccountStates.HasEnded(new DateOnly(2026, 10, 19), Today));
    }
}
