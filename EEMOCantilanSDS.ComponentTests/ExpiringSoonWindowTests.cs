using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// "Expiring soon" means the same thing on every screen, and it means what the DOMAIN says.
///
/// <para>
/// Found while auditing the portal's clock reads. The stall profile decided this for itself, and got it wrong twice: it wrote the
/// renewal window as a literal <c>3</c> months, and it left out the "not already expired" half of the rule. A term that ran out two
/// years ago was therefore labelled <b>Expiring soon</b> — the one thing that badge exists to distinguish. The vendor registry had
/// the logic right but also hardcoded the 3.
/// </para>
///
/// <para>
/// <see cref="Contract.IsExpiringSoonOn"/> is the rule: <c>!IsExpiredOn(asOf) &amp;&amp; ExpiryDate &lt;= asOf.AddMonths(
/// DomainRules.ExpiringSoonMonths)</c>. What is asserted here is that rule's behaviour at the boundaries, and that the window is the
/// shared constant rather than a number typed into a page — so moving the office's renewal window moves every screen with it.
/// </para>
/// </summary>
public class ExpiringSoonWindowTests
{
    private static readonly DateOnly Today = new(2026, 8, 17);

    private static Contract TermExpiringOn(DateOnly expiry)
    {
        // Worked backwards from the wanted expiry: Create sets ExpiryDate = EffectivityDate.AddYears(DurationYears).
        var years = 3;
        return Contract.Create(
            Guid.NewGuid(), "Merlita A. Abuso", "Merlita A. Abuso",
            expiry.AddYears(-years), durationYears: years, monthlyRate: 900m);
    }

    [Fact]
    public void TheWindowIsTheSharedConstant_NotANumberInAPage()
    {
        // If this ever stops being 3, the profile and the vendor registry must move with it. They now read this constant, so they
        // will; a literal in the markup would not have.
        Assert.Equal(3, DomainRules.ExpiringSoonMonths);
    }

    [Fact]
    public void ATermRunningOutInsideTheWindowIsExpiringSoon()
    {
        var contract = TermExpiringOn(Today.AddMonths(DomainRules.ExpiringSoonMonths).AddDays(-1));

        Assert.True(contract.IsExpiringSoonOn(Today));
        Assert.False(contract.IsExpiredOn(Today));
    }

    [Fact]
    public void ATermRunningOutBEYONDTheWindowIsNot()
    {
        var contract = TermExpiringOn(Today.AddMonths(DomainRules.ExpiringSoonMonths).AddDays(1));

        Assert.False(contract.IsExpiringSoonOn(Today));
    }

    [Fact]
    public void ATermThatHasALREADYRUNOUTIsExpiredAndNotExpiringSoon()
    {
        // The fault the profile page had. An expired term is not "expiring soon" — it has expired, and the office needs to be told
        // that, not offered a renewal window that closed long ago.
        var contract = TermExpiringOn(Today.AddYears(-2));

        Assert.True(contract.IsExpiredOn(Today));
        Assert.False(contract.IsExpiringSoonOn(Today));
    }

    [Fact]
    public void AnOpenEndedOccupancyIsNeverExpiringSoon()
    {
        // A space let without a signed contract runs until the office ends it, so there is no renewal to chase.
        var openEnded = Contract.Create(
            Guid.NewGuid(), "Merlita A. Abuso", null, Today.AddYears(-5),
            durationYears: 0, monthlyRate: 900m, arrangement: OccupancyArrangement.SpaceOnly);

        Assert.False(openEnded.IsExpiringSoonOn(Today));
        Assert.False(openEnded.IsExpiredOn(Today));
    }

    [Fact]
    public void NeitherSCREENHoldsItsOwnCopyOfTheRule()
    {
        // Asserted against the markup, because that is where both faults lived and the domain tests above cannot see them: the
        // pages compute the badge in their own expressions. Proven necessary by putting the old expression back — the domain tests
        // stayed green while an expired term was mislabelled again.
        foreach (var page in new[]
                 {
                     Path.Combine("Components", "Pages", "Shared", "Actions", "Profile.razor"),
                     Path.Combine("Components", "Pages", "Menus", "Vendor.razor"),
                 })
        {
            var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client", page));

            Assert.DoesNotContain("AddMonths(3)", markup);
            Assert.Contains("AddMonths(DomainRules.ExpiringSoonMonths)", markup);
        }
    }

    [Fact]
    public void TheProfileStillRequiresTheTermNotToHaveExpired()
    {
        // The half that was missing. Without it a term that ran out years ago was badged "Expiring soon".
        var markup = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Shared", "Actions", "Profile.razor"));

        Assert.Contains("var expiringSoon = expiry.HasValue && !expired", markup);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
