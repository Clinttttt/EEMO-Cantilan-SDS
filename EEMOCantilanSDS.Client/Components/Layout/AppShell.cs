namespace EEMOCantilanSDS.Client.Components.Layout;

/// <summary>
/// Which pages stand alone, without the app shell (sidebar) around them.
///
/// <para>
/// A pure function rather than a condition buried in the layout, because it is a rule that breaks quietly: a page added to
/// the portal without being listed here gets a sidebar it should not have, and a mistake in the other direction hides the
/// navigation from the whole application. Neither shows up as an error — only as a wrong-looking screen — so it is worth
/// being able to state and test in both directions.
/// </para>
/// </summary>
public static class AppShell
{
    /// <summary>
    /// Paths that render on their own. Two kinds: pages reached before sign-in, and <c>/change-password</c>, where the account
    /// IS signed in but the API refuses every other endpoint until an office-issued password is replaced — a sidebar there
    /// offers a menu on which nothing works.
    /// </summary>
    private static readonly string[] StandalonePaths =
    [
        "/login",
        "/setup",
        "account-setup-admin",
        "/activate",
        "/payor",
        "/forgot-password",
        "/reset-password",
        "/verify-email",
        "/change-password",
    ];

    /// <summary>
    /// True when <paramref name="absolutePath"/> is one of the standalone pages. Matching is case-insensitive and by
    /// substring, so a route with a token or a child segment (<c>/reset-password/abc123</c>) is still recognised.
    /// </summary>
    public static bool IsStandalonePage(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        var path = absolutePath.ToLowerInvariant();
        return StandalonePaths.Any(path.Contains);
    }
}
