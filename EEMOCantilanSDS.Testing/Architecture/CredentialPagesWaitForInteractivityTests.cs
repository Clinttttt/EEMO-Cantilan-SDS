namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A page that takes credentials must not accept text it is about to throw away.
/// </summary>
/// <remarks>
/// Reported from use as the login form "refreshing" and losing what had been typed. The cause is not the network, though a slow
/// connection is what makes it visible:
///
/// <para>1. <c>App.razor</c> renders with <c>InteractiveServerRenderMode(prerender: true)</c>, so the page arrives as static HTML.
/// 2. The boot splash hides itself the moment prerendered markup is present - it looks for <c>.setup-root</c> among others.
/// 3. Every one of these pages IS <c>.setup-root</c>.
/// 4. So a complete but DEAD form is on screen before the SignalR circuit connects, and Blazor's interactive first render then
/// replaces those inputs with the component's own state, which is empty.</para>
///
/// <para>The fix each page carries is a <c>FormReady</c> property reading <c>RendererInfo.IsInteractive</c>, with its inputs and
/// submit disabled until it is true. This test holds that, because the pages share only a convention: nothing in the compiler
/// requires a new credential page to remember it, and the failure is silent - the form looks perfect and quietly discards work.</para>
///
/// <para>Read as text, because these are Razor pages that no test project renders. Blunt, but it names the six that matter and
/// checks that each still has both halves.</para>
/// </remarks>
public class CredentialPagesWaitForInteractivityTests
{
    /// <summary>
    /// The pages that take credentials and sit on <c>.setup-root</c>.
    /// </summary>
    /// <remarks>
    /// <c>VerifyEmail</c> is deliberately absent: it is the same root but has no inputs at all, so it has nothing to lose.
    /// </remarks>
    private static readonly string[] CredentialPages =
    [
        "Login.razor",
        "AccountSetup.razor",
        "AdminActivate.razor",
        "ChangePassword.razor",
        "ForgotPassword.razor",
        "ResetPassword.razor",
    ];

    [Fact]
    public void EveryCredentialPageAsksTheFrameworkWhetherItIsInteractive()
    {
        var offenders = ReadPages()
            .Where(p => !p.Text.Contains("RendererInfo.IsInteractive"))
            .Select(p => p.Page)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These credential pages do not wait for the render to be interactive, so anything typed before the circuit connects is "
            + "discarded:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryCredentialPageActuallyGatesAControlOnIt()
    {
        // The property alone changes nothing: it has to reach the inputs and the submit. Two, because one would be satisfied by
        // gating the button and leaving the fields open - which is the case that loses work.
        var offenders = ReadPages()
            .Where(p => System.Text.RegularExpressions.Regex.Matches(p.Text, "!FormReady").Count < 2)
            .Select(p => p.Page)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These pages declare the interactivity check but barely use it, so input is still accepted before it can be kept:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Every page named here must exist, or the rules above cover nothing.</summary>
    [Fact]
    public void EveryPageNamedHereStillExists()
    {
        var missing = CredentialPages
            .Where(page => Locate(page) is null)
            .ToList();

        Assert.True(missing.Count == 0,
            "These pages are named by this test but no longer exist:\n  " + string.Join("\n  ", missing));
    }

    private static IEnumerable<(string Page, string Text)> ReadPages()
    {
        foreach (var page in CredentialPages)
        {
            var path = Locate(page);
            if (path is not null)
                yield return (page, File.ReadAllText(path));
        }
    }

    private static string? Locate(string page)
    {
        var root = Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages");

        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, page, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    /// <summary>Walks up from the test binaries to the solution directory.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
