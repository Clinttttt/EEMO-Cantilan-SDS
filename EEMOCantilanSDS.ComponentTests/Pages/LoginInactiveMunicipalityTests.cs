namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// A municipality that has not been activated is sent to onboarding, not offered a sign-in it cannot complete.
///
/// <para>
/// The login page read an LGU's branding for its name and ignored the status that came with it, so
/// <c>/login?lgu=CARRASCAL</c> served a full sign-in form for a municipality with no office, no accounts and no seal. The office
/// typed credentials that could not exist and got a bare 401 — correct of the API, and useless to the person reading it.
/// </para>
///
/// <para>
/// Verified live as a 302 to the onboarding page for all four unactivated municipalities, while Cantilan and a plain
/// <c>/login</c> still answer 200. What is asserted HERE is the one thing that live check could not have caught earlier: that the
/// redirect is not buried inside the <c>catch</c> that guards the branding fetch.
/// </para>
/// </summary>
public class LoginInactiveMunicipalityTests
{
    private static string LoginMarkup() => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Login.razor"));

    [Fact]
    public void TheRedirectIsNotSwallOWEDByTheBrandingCatch()
    {
        // The trap this fell into, and it is invisible in review. NavigateTo signals a redirect during static rendering by
        // THROWING NavigationException, so a redirect written inside the try/catch that guards the branding fetch is caught and
        // discarded: the page carries on and serves the form. The first version did exactly that, built clean, read correctly,
        // and did nothing at all — found only by running the app and seeing 200 where 302 was expected.
        var markup = LoginMarkup();

        var catchIndex = markup.IndexOf("catch { /* keep default branding on any failure */ }", StringComparison.Ordinal);
        var redirectIndex = markup.IndexOf("Navigation.NavigateTo(OnboardingLinks.Base", StringComparison.Ordinal);

        Assert.True(catchIndex > 0, "The branding fetch's catch block was not found; this test needs rewriting.");
        Assert.True(redirectIndex > 0, "The onboarding redirect was not found.");
        Assert.True(redirectIndex > catchIndex,
            "The onboarding redirect must sit AFTER the branding catch block. Inside it, NavigationException is swallowed and " +
            "the redirect silently does nothing.");
    }

    [Fact]
    public void TheDestinationIsTheSharedOnboardingLink()
    {
        // Not a literal URL. OnboardingLinks already owns this address and is environment-driven (ONBOARDING_LINK_BASE), so a
        // deployment pointing elsewhere moves this redirect with it instead of leaving one page behind on the old domain.
        Assert.Contains("OnboardingLinks.Base", LoginMarkup());
        Assert.DoesNotContain("www.stalltrack.site/onboarding", LoginMarkup());
    }

    [Fact]
    public void OnlyTheSelectorsOwnLinkCanForwardAnybody()
    {
        // Typing a username must never send somebody off the page. Branding resolves from what has been typed so far, so a
        // half-finished "carrascal.head" would throw a person out of a login they were in the middle of. The redirect therefore
        // lives in the ?lgu= branch, which is the municipality selector's own link.
        var markup = LoginMarkup();

        var lguBranch = markup.IndexOf("query.TryGetValue(\"lgu\"", StringComparison.Ordinal);
        var redirect = markup.IndexOf("Navigation.NavigateTo(OnboardingLinks.Base", StringComparison.Ordinal);
        var usernameHandler = markup.IndexOf("async Task OnUsernameChanged()", StringComparison.Ordinal);

        Assert.True(lguBranch > 0 && redirect > lguBranch,
            "The redirect must live inside the ?lgu= branch.");
        Assert.True(usernameHandler < lguBranch || redirect < usernameHandler || usernameHandler > redirect,
            "The redirect must not be reachable from the username handler.");
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
