using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The identifier carried by a space the office does not number.
///
/// <para>
/// A space let WITHOUT a signed contract — a barbecue stand, an ice-plant space, a commercial-centre space held on an
/// extension — has no number on the office's own list; the sheet leaves the contract columns blank. The system needs
/// an identifier all the same, and it used to take the next ordinary stall number: an un-numbered commercial-centre
/// space was recorded as "4", the actual stall 4 could then not be registered because the number was reported
/// occupied, and the sheet blanks the number on such a row, so the office could not see which of its numbers had gone.
/// </para>
/// </summary>
public class SpaceNumberTests
{
    [Fact]
    public void ASpaceIdentifierIsRecognised_AndAStallNumberIsNot()
    {
        Assert.True(SpaceNumber.IsSpace("SP-1"));
        Assert.True(SpaceNumber.IsSpace("sp-12"));      // the clerk may retype it in either case
        Assert.True(SpaceNumber.IsSpace("  SP-3"));
        Assert.False(SpaceNumber.IsSpace("4"));
        Assert.False(SpaceNumber.IsSpace("101"));
        Assert.False(SpaceNumber.IsSpace(""));
        Assert.False(SpaceNumber.IsSpace(null));
    }

    [Fact]
    public void TheSeriesCannotCollideWithAStallNumber()
    {
        // The whole point: whatever ordinal it reaches, it is never equal to a number the office might issue.
        for (var i = 1; i <= 50; i++)
        {
            var space = SpaceNumber.Format(i);
            Assert.NotEqual(i.ToString(), space);
            Assert.True(SpaceNumber.IsSpace(space));
        }
    }

    [Fact]
    public void TheNextOrdinalContinuesTheSpaceSeriesAndIgnoresStallNumbers()
    {
        var existing = new[] { "1", "2", "17", "SP-1", "SP-2", "101" };

        // Continues at SP-3 — it does not jump to 102 and does not restart at SP-1 over an existing space.
        Assert.Equal(2, SpaceNumber.HighestOrdinal(existing));
        Assert.Equal("SP-3", SpaceNumber.Format(SpaceNumber.HighestOrdinal(existing) + 1));
    }

    [Fact]
    public void NoSpacesYetMeansTheSeriesStartsAtOne()
    {
        Assert.Equal(0, SpaceNumber.HighestOrdinal(new[] { "1", "2", "3" }));
        Assert.Equal("SP-1", SpaceNumber.Format(SpaceNumber.HighestOrdinal(Array.Empty<string>()) + 1));
    }

    [Fact]
    public void AMalformedEntryIsIgnoredRatherThanStoppingAnImport()
    {
        // The column is free text the office types into. A bad entry must not throw in the middle of a batch.
        Assert.Equal(4, SpaceNumber.HighestOrdinal(new[] { "SP-", "SP-abc", "SP-4", null, "  ", "SP-2" }));
    }

    [Fact]
    public void ASpaceShowsNothingToTheOffice_WhileAStallShowsItsNumber()
    {
        // The office's own list leaves the column empty for a space it does not number and identifies the occupancy by
        // the lessee. The identifier still exists behind this and is still what the system keys, links and routes on —
        // it is simply not a fact the office would recognise, so it is not put in front of them.
        Assert.Equal(string.Empty, SpaceNumber.Display("SP-1"));
        Assert.Equal(string.Empty, SpaceNumber.Display("sp-12"));
        Assert.Equal("4", SpaceNumber.Display("4"));
        Assert.Equal("101", SpaceNumber.Display("101"));

        // And nothing reads "Stall SP-1" either: naming it would assert the numbering that does not exist.
        Assert.Equal(string.Empty, SpaceNumber.Describe("SP-1"));
        Assert.Equal("Stall 4", SpaceNumber.Describe("4"));
        Assert.Equal("Stall 101", SpaceNumber.Describe("101"));
    }

    [Fact]
    public void TheIdentifierStillExists_EvenThoughItIsNeverShown()
    {
        // Blanking the display must not blank the key: the system needs this value to link, dedupe and route.
        var assigned = SpaceNumber.Format(3);
        Assert.Equal("SP-3", assigned);
        Assert.True(SpaceNumber.IsSpace(assigned));
        Assert.Equal(3, SpaceNumber.HighestOrdinal(new[] { assigned }));
        Assert.Equal(string.Empty, SpaceNumber.Display(assigned));
    }
}
