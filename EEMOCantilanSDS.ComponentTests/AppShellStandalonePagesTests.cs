using EEMOCantilanSDS.Client.Components.Layout;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Which pages render without the app shell.
///
/// <para>
/// Tested in BOTH directions on purpose. This rule fails silently: a page missing from the list gets a sidebar it should not
/// have, and a rule that matched too much would hide the navigation from the entire portal. Neither raises an error — only a
/// wrong-looking screen — and asserting one direction alone would let a rule that always returns the same answer pass.
/// </para>
/// </summary>
public class AppShellStandalonePagesTests
{
    [Theory]
    // Reached before sign-in.
    [InlineData("/login")]
    [InlineData("/setup")]
    [InlineData("/account-setup-admin")]
    [InlineData("/activate/abc123")]
    [InlineData("/forgot-password")]
    [InlineData("/reset-password/tok-9")]
    [InlineData("/verify-email/tok-9")]
    [InlineData("/payor/dashboard")]
    // Signed in, but the API refuses every other endpoint until the office-issued password is replaced, so a menu there
    // would offer destinations that cannot answer.
    [InlineData("/change-password")]
    [InlineData("/Change-Password")]
    public void ThesePagesStandAlone(string path) => Assert.True(AppShell.IsStandalonePage(path));

    [Theory]
    [InlineData("/menu")]
    [InlineData("/dashboard")]
    [InlineData("/collectors")]
    [InlineData("/vendors-and-stalls")]
    [InlineData("/transactions")]
    [InlineData("/online-payments")]
    [InlineData("/financial-reports")]
    [InlineData("/audit-trail")]
    [InlineData("/settings")]
    [InlineData("/")]
    public void TheRestKeepTheAppShell(string path) => Assert.False(AppShell.IsStandalonePage(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotAStandalonePage(string? path)
    {
        // A blank path must not be treated as standalone: that would strip the navigation from an unrecognised route rather
        // than leaving the shell in place.
        Assert.False(AppShell.IsStandalonePage(path));
    }
}
