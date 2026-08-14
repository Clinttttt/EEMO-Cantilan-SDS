using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The rule that decides when two typed names are the same person. It sits under the slaughterhouse OR check and under every
/// per-client total, so its edges are worth stating rather than assuming.
/// </summary>
public class PersonNameTests
{
    [Theory]
    [InlineData("Alan Cayetano", "Alan Cayetano")]
    [InlineData("  Alan Cayetano  ", "Alan Cayetano")]
    [InlineData("Alan   Cayetano", "Alan Cayetano")]
    [InlineData("Alan\tCayetano", "Alan Cayetano")]
    [InlineData("Maria  de los  Santos", "Maria de los Santos")]
    [InlineData("ALAN CAYETANO", "ALAN CAYETANO")]   // capitalisation is preserved, not folded
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CanonicalTrimsAndCollapsesButKeepsCase(string? raw, string expected)
        => Assert.Equal(expected, PersonName.Canonical(raw));

    [Theory]
    [InlineData("Alan Cayetano", "alan cayetano")]
    [InlineData("ALAN CAYETANO", "alan cayetano")]
    [InlineData("  Alan  Cayetano ", "alan cayetano")]
    public void MatchKeyIsTheCanonicalFormLowercased(string raw, string expected)
        => Assert.Equal(expected, PersonName.MatchKey(raw));

    [Theory]
    [InlineData("Juan Dela Cruz", "Juan dela Cruz")]
    [InlineData("Juan Dela Cruz", "JUAN DELA CRUZ")]
    [InlineData("Juan Dela Cruz", "  Juan   Dela Cruz  ")]
    public void TheSamePersonSpelledDifferentlyMatches(string left, string right)
        => Assert.True(PersonName.Matches(left, right));

    [Theory]
    [InlineData("Juan Dela Cruz", "Juan Dela Cruz Jr")]
    [InlineData("Juan Dela Cruz", "Juana Dela Cruz")]
    [InlineData("Juan Dela Cruz", "Pedro Dela Cruz")]
    [InlineData("Juan Dela Cruz", "JuanDela Cruz")]      // a missing space is a different string, not a spelling variant
    public void DifferentPeopleDoNotMatch(string left, string right)
        => Assert.False(PersonName.Matches(left, right));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Alan Cayetano", null)]
    public void BlanksNeverMatch(string? left, string? right)
    {
        // Two unnamed records are not "the same person": treating blanks as equal would let an OR be shared by every
        // transaction that happened to be missing a name.
        Assert.False(PersonName.Matches(left, right));
    }

    [Fact]
    public void ComparerGroupsSpellingsAsOneClient()
    {
        var entries = new[] { "Juan Dela Cruz", "juan dela cruz", "JUAN  DELA CRUZ", "Pedro Reyes" };

        var groups = entries.GroupBy(PersonName.Canonical, PersonName.Comparer).ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal(3, groups.Single(g => PersonName.Matches(g.Key, "Juan Dela Cruz")).Count());
    }

    [Fact]
    public void ComparerAgreesWithMatchesOnHashing()
    {
        // A comparer whose hash disagrees with its equality silently splits groups only for some inputs, which is worse than
        // a plain failure: totals would be right in tests and wrong in production.
        Assert.Equal(
            PersonName.Comparer.GetHashCode("  JUAN  dela cruz "),
            PersonName.Comparer.GetHashCode("Juan Dela Cruz"));
    }
}
