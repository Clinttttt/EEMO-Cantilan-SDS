namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A collection sheet must never be able to lock the collector out.
/// </summary>
/// <remarks>
/// A collector reported the market sheet sticking on "Saving..." with nothing clickable but the app switcher. The cause was not a
/// slow network: the HTTP layer throws on a timeout, nothing caught it, so the saving flag stayed set for the life of the page -
/// and Cancel was disabled beside Confirm, so there was no way out of the sheet at all. Killing the app was the only exit, and the
/// round stopped at that stall.
///
/// <para>The same shape sat on nine more sheets across four pages, each with a <c>try/finally</c> that wrapped only the reload
/// after the write rather than the write itself. This test states the two rules that keep it fixed, because it is an easy thing to
/// reintroduce: the obvious way to write one of these handlers is to set the flag, call the API, and clear the flag on the way
/// out.</para>
///
/// <para>It reads the .razor files as text. That is blunt, but these are MAUI pages: no test project references the Mobile app and
/// no harness renders them, so text is the only place this rule can be enforced at all. Blunt and enforced beats elegant and
/// absent.</para>
/// </remarks>
public class CollectionSheetsCannotLockTheCollectorOutTests
{
    /// <summary>The collection pages of the collector's app: every screen that takes money.</summary>
    private static readonly string[] CollectionPages =
    [
        "Market.razor",
        "MonthlyCollection.razor",
        "Slaughter.razor",
        "Taboan.razor",
        "Terminal.razor",
    ];

    /// <summary>
    /// Cancel must not be disabled by the flag that Confirm is disabled by.
    /// </summary>
    /// <remarks>
    /// Backing out of a sheet cannot lose money: it holds an answer the server has not accepted yet, and a write already sent
    /// either lands or is queued whatever the screen does. So there is nothing to protect by disabling Cancel, and everything to
    /// lose - it is the collector's only way out while a save is in flight.
    ///
    /// <para>Two Cancels are still disabled while their own work runs, and both are allowed: the activation-code generator and the
    /// online-payment OR encoder already wrap their writes in try/catch/finally, so their flags always clear and no wedge is
    /// possible. Only the payment sheets are policed here.</para>
    /// </remarks>
    [Fact]
    public void CancelIsNeverDisabledWhileASaveIsInFlight()
    {
        var offenders = new List<string>();

        foreach (var (page, text) in ReadCollectionPages())
        {
            foreach (var line in text.Split('\n'))
            {
                if (!line.Contains("btn-cancel"))
                    continue;

                if (line.Contains("disabled=\"@IsSaving\""))
                    offenders.Add($"{page}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A sheet's Cancel is disabled while it saves, so a save that hangs leaves the collector no way out of it:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// A page that saves must also handle the failure that saving throws.
    /// </summary>
    /// <remarks>
    /// The write throws on a timeout or an unreachable server. A page that sets a saving flag without catching that is one
    /// mistake away from the wedge again, so every page that saves is required to name the connectivity-shaped failure - which is
    /// the guard that clears the flag and either queues the work or states plainly that nothing was recorded.
    /// </remarks>
    [Fact]
    public void EveryPageThatSavesHandlesTheFailureThatSavingThrows()
    {
        var offenders = new List<string>();

        foreach (var (page, text) in ReadCollectionPages())
        {
            if (!text.Contains("IsSaving = true"))
                continue;

            if (!text.Contains("IsConnectivityShaped"))
                offenders.Add(page);
        }

        Assert.True(offenders.Count == 0,
            "These pages set a saving flag but never handle a timeout, so one will leave the sheet locked:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The guard must stay narrow: a definitive refusal from the server is never queued.
    /// </summary>
    /// <remarks>
    /// Queueing is for a signal too thin to reach the office. A server that ANSWERED - already collected, outside the term, a day
    /// in the future - has ruled, and its ruling belongs on the screen. Queue it and the refusal replays every time the signal
    /// returns, for ever. So a page that queues on failure must decide with the transient test rather than on any failure at all.
    /// </remarks>
    [Fact]
    public void OnlyTransientFailuresAreQueued()
    {
        var offenders = new List<string>();

        foreach (var (page, text) in ReadCollectionPages())
        {
            if (!text.Contains("IsConnectivityShaped"))
                continue;

            if (!text.Contains("IsTransientFailure"))
                offenders.Add(page);
        }

        Assert.True(offenders.Count == 0,
            "These pages guard against a timeout but do not separate a transient failure from the server's own refusal:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every page this test names must exist.
    /// </summary>
    /// <remarks>
    /// A dead entry would make the rules above pass by reading nothing at all, which is the failure mode of every list-of-names
    /// test. If a page is renamed or retired, this fails and the list is corrected deliberately.
    /// </remarks>
    [Fact]
    public void EveryPageNamedHereStillExists()
    {
        var missing = CollectionPages
            .Where(page => !File.Exists(Path.Combine(PagesDirectory(), page)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These pages are named by this test but no longer exist, so its rules cover nothing:\n  "
            + string.Join("\n  ", missing));
    }

    private static IEnumerable<(string Page, string Text)> ReadCollectionPages()
    {
        var dir = PagesDirectory();

        foreach (var page in CollectionPages)
        {
            var path = Path.Combine(dir, page);
            if (File.Exists(path))
                yield return (page, File.ReadAllText(path));
        }
    }

    private static string PagesDirectory() => Path.Combine(
        RepositoryRoot(), "EEMOCantilanSDS.Mobile", "Components", "Pages", "Menus");

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
