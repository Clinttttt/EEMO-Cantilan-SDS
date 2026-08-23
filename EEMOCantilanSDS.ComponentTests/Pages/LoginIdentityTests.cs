using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Whose identity a sign-in page carries, and how it knows.
///
/// <para>
/// An office activated Madrid, pressed "Continue to sign in", and was greeted by the Municipality of Cantilan - seal, name
/// and office. The same happened on signing out. Nothing was wrong with the branding machinery: the login page simply had
/// no way of knowing which LGU it was being opened for, so it fell back to the default municipality's identity, which is
/// the office's sanctioned fallback for a genuinely unknown visitor. The fix is to make "unknown" rare rather than to
/// change what unknown looks like.
/// </para>
///
/// <para>
/// Three ways in, in order of authority: the address (<c>?lgu={code}</c>) which also scopes the sign-in; the LGU that last
/// signed in on this browser, which is branding only; and the typed username, which still helps and is no longer relied
/// upon - the console has stopped inventing "{municipality}.head" usernames to feed it.
/// </para>
///
/// <para>
/// Asserted against the page's own source, which is this file's established approach for Login.razor: the behaviour lives
/// in an initialise path that reaches the API and the request's cookies, and the properties that matter here are
/// structural - which branch runs when, and what it is forbidden to touch.
/// </para>
///
/// <para>
/// Only this repository's own files. An earlier version of this file also asserted that the admin console had stopped
/// pre-filling "{municipality}.head", by reading activation.ts out of the sibling platform repository. It passed here and
/// failed in CI, which checks out this repository alone - so the assertion belongs to the repository that owns the file.
/// </para>
/// </summary>
public class LoginIdentityTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    private static string LoginMarkup() =>
        Source("EEMOCantilanSDS.Client", "Components", "Pages", "Login.razor");

    [Fact]
    public void TheRememberedLGUIsUsedWhenTheAddressNamesNone()
    {
        // The case the office reported after signing out, and the one a link cannot cover: a bookmarked /login, or simply
        // the morning after.
        var markup = LoginMarkup();

        Assert.Contains("AuthProxyController.LastMunicipalityCookie", markup);
        Assert.Contains("GetBrandingByIdentifierAsync(remembered", markup);
    }

    [Fact]
    public void TheREMEMBEREDLGUNeverScopesTheSignIn()
    {
        // The safety property of the whole idea. If the remembered code were assigned to LguParam, the sign-in would be
        // scoped to a municipality nobody chose - so a clerk from another LGU sharing the office browser would have their
        // account looked up in the wrong tenant and be refused. It decorates the page; it does not decide anything.
        var markup = LoginMarkup();

        var branch = Regex.Match(markup,
            @"else if \(string\.IsNullOrEmpty\(PBrandName\)\)\s*\{.*?\n        \}",
            RegexOptions.Singleline).Value;

        Assert.False(string.IsNullOrEmpty(branch), "The remembered-LGU branch was not found; this test needs rewriting.");
        Assert.DoesNotContain("LguParam =", branch);
        Assert.DoesNotContain("_lguLocked = true", branch);
    }

    [Fact]
    public void TheADDRESSWinsOverWhatTheBrowserRemembers()
    {
        // ?lgu= is the municipality selector's own link and the scoped sign-in. The remembered value is only consulted
        // when the address names nobody, which is what makes it an "else if" rather than a second opinion.
        var markup = LoginMarkup();

        var lguIndex = markup.IndexOf("if (query.TryGetValue(\"lgu\", out var lguValues))", StringComparison.Ordinal);
        var rememberedIndex = markup.IndexOf("else if (string.IsNullOrEmpty(PBrandName))", StringComparison.Ordinal);

        Assert.True(lguIndex > 0, "The ?lgu branch was not found.");
        Assert.True(rememberedIndex > lguIndex, "The remembered-LGU branch must be the fallback of the ?lgu branch.");
    }

    [Fact]
    public void SigningOutReturnsToTheOFFICESOwnLoginPage()
    {
        // It used to return everyone to a bare /login. The tenant code is read from the session's own claim BEFORE the
        // session is torn down, because afterwards there is nothing left to read.
        var source = Source("EEMOCantilanSDS.Client", "Securities", "AuthService.cs");

        Assert.Contains("/login?lgu=", source);
        Assert.Contains("AppClaimTypes.Municipality", source);

        var read = source.IndexOf("await CurrentTenantCodeAsync()", StringComparison.Ordinal);
        var clear = source.IndexOf("tokenService.Clear()", StringComparison.Ordinal);
        Assert.True(read > 0 && clear > read, "The tenant code must be read before the session is cleared.");
    }

    [Fact]
    public void ActivationHandsTheLoginPageTheLGUItJustActivated()
    {
        var markup = Source("EEMOCantilanSDS.Client", "Components", "Pages", "AdminActivate.razor");

        Assert.Contains("lgu=", markup);
        Assert.Contains("Context?.Code", markup);
    }

    [Fact]
    public void TheActivationContextCarriesTheMunicipalitysCode()
    {
        // The name alone is not enough: the login page resolves an LGU by its identifier, and inferring a code from a
        // display name is the kind of guess that works until a municipality is renamed.
        var query = Source("EEMOCantilanSDS.Application", "Queries", "Onboarding", "GetActivationContext",
            "GetActivationContextQuery.cs");
        var handler = Source("EEMOCantilanSDS.Application", "Queries", "Onboarding", "GetActivationContext",
            "GetActivationContextQueryHandler.cs");

        Assert.Contains("string? Code", query);
        Assert.Contains("m.Code", handler);
    }

    [Fact]
    public void TheActivationPageCarriesTheOfficesOwnSeal()
    {
        // An office setting its first password was shown StallTrack's mark and nothing of its own, on the grounds that a
        // one-time token does not name an LGU. It does, by way of the account it belongs to — so the context carries the
        // office's seal, and the page shows it beside the platform's.
        var query = Source("EEMOCantilanSDS.Application", "Queries", "Onboarding", "GetActivationContext",
            "GetActivationContextQuery.cs");
        var handler = Source("EEMOCantilanSDS.Application", "Queries", "Onboarding", "GetActivationContext",
            "GetActivationContextQueryHandler.cs");
        var markup = Source("EEMOCantilanSDS.Client", "Components", "Pages", "AdminActivate.razor");

        Assert.Contains("string? SealPath", query);
        Assert.Contains("m.SealPath", handler);
        Assert.Contains("Context.Municipality seal", markup);

        // An office with no seal on file gets the waiting slot, never another municipality's mark.
        Assert.Contains("seal-placeholder", markup);
        Assert.DoesNotContain("stalltrack-seal.png", markup);
    }

    [Fact]
    public void TheActivationPageDoesNotFillInTheUsername()
    {
        // The account is provisioned under a name derived from the LGU. Showing it invited an office to accept a sign-in
        // name it never chose, so the field starts empty and the office types its own.
        var markup = Source("EEMOCantilanSDS.Client", "Components", "Pages", "AdminActivate.razor");

        Assert.DoesNotContain("Username = Context?.Username", markup);
        Assert.Contains("Choose your sign-in username", markup);
    }

    [Fact]
    public void TypingIsNotOverwrittenByTheServersOlderValue()
    {
        // Reported from use: characters vanished and came back while typing. Written as value="@Field" with @oninput,
        // every keystroke re-rendered and re-applied the value as of that render, so on a slow circuit the server's
        // older value overwrote what had been typed since. @bind tracks what it last put in the element.
        var markup = Source("EEMOCantilanSDS.Client", "Components", "Pages", "AdminActivate.razor");

        // Comments out: this page explains the very pattern being banned, and a comment is not markup.
        var code = Regex.Replace(markup, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotMatch(@"value=""@(Username|Password|ConfirmPassword)""", code);
        Assert.Contains(@"@bind=""Username""", code);
        Assert.Contains(@"@bind=""Password""", code);
        Assert.Contains(@"@bind=""ConfirmPassword""", code);
    }

    [Fact]
    public void ContinuingToSignInReloadsSoTheOfficesOwnSealIsInTheFirstByte()
    {
        // Navigating inside the circuit built the login page in the browser, and its first frame came from the field
        // initialisers - the default LGU - before the branding call returned, so an office that had just activated saw
        // Cantilan's seal flash. A real request lets the server prerender the page with this LGU already resolved.
        var markup = Source("EEMOCantilanSDS.Client", "Components", "Pages", "AdminActivate.razor");

        Assert.Matches(@"lgu=\{Uri\.EscapeDataString\(code!\)\}"",\s*forceLoad: true", markup);
    }

    [Fact]
    public void TheActivatedPanelIsNotWashedInNavy()
    {
        // The confirmation was the one screen in the sequence with a dark panel: a light password step, then this, then a
        // light login. Asserted on the stylesheet, because a background is not visible to a render test.
        var css = Source("EEMOCantilanSDS.Client", "Components", "Pages", "AdminActivate.razor.css");

        var activated = Regex.Match(css, @"\.setup-right-activated\s*\{[^}]*\}").Value;

        Assert.True(string.IsNullOrEmpty(activated),
            "The activated state should not restate a background at all - it inherits the panel's own light surface.");
        Assert.DoesNotContain("municipality-page-background.png", css);
    }
}
