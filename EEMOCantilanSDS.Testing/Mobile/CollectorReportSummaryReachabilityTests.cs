using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Every list the collector's Reports screen can open must still be reachable from its summary.
/// </summary>
/// <remarks>
/// The summary was rebuilt on 2026-09-06: ten identical rows became a primary figure, two tiles, one payor block and a plain
/// line, because every number had carried the same weight and the sum a collector remits sat level with a count of excused
/// records.
///
/// <para>THE RISK IN THAT KIND OF CHANGE IS NOT HOW IT LOOKS. It is an entry point quietly disappearing: the detail view can
/// still title and render a list that nothing on the screen opens any more, so the data is there and unreachable, and nobody
/// notices because the screen looks tidier than before. This holds the two sides together - every card the detail view knows how
/// to title must be opened by something in the summary.</para>
///
/// <para>Read as text, because the mobile UI has no automated coverage.</para>
/// </remarks>
public class CollectorReportSummaryReachabilityTests
{
    private static string ReportPage()
    {
        var path = Path.Combine(
            RepositoryRoot(), "EEMOCantilanSDS.Mobile", "Components", "Pages", "Menus", "Report.razor");

        Assert.True(File.Exists(path), $"Report.razor was not found at {path}.");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The cards the detail view can title — taken from the page itself rather than listed here.</summary>
    /// <remarks>
    /// Read from the source so the test cannot fall behind the page: a card added to the detail titles is covered the moment it
    /// is added, without anybody remembering to come here.
    /// </remarks>
    private static IReadOnlyList<string> TitledCards(string page) =>
        Regex.Matches(page, @"^\s*""(?<card>[a-z-]+)""\s*=>\s*""", RegexOptions.Multiline)
            .Select(m => m.Groups["card"].Value)
            .Distinct()
            .ToList();

    private static IReadOnlyList<string> OpenedCards(string page) =>
        Regex.Matches(page, @"OpenDetail\(""(?<card>[a-z-]+)""\)")
            .Select(m => m.Groups["card"].Value)
            .Distinct()
            .ToList();

    [Fact]
    public void EveryDetailListTheScreenCanTitleIsOpenedBySomething()
    {
        var page = ReportPage();

        var titled = TitledCards(page);
        Assert.True(titled.Count >= 7, $"Expected the detail titles to be readable from the page; found {titled.Count}.");

        var opened = OpenedCards(page);
        var orphaned = titled.Where(card => !opened.Contains(card)).ToList();

        Assert.True(orphaned.Count == 0,
            "These detail lists can be titled and rendered but nothing on the summary opens them, so the figures behind them are "
            + "unreachable:\n  " + string.Join("\n  ", orphaned));
    }

    /// <summary>
    /// And nothing opens a list the screen cannot title, which would show the office a blank sheet.
    /// </summary>
    [Fact]
    public void NothingOpensAListTheScreenCannotTitle()
    {
        var page = ReportPage();

        var titled = TitledCards(page);
        var unknown = OpenedCards(page).Where(card => !titled.Contains(card)).ToList();

        Assert.True(unknown.Count == 0,
            "The summary opens these detail lists, but the detail view has no title for them:\n  " + string.Join("\n  ", unknown));
    }

    /// <summary>
    /// The stack of look-alike rows is gone, and the collector's own figure is stated apart from the rest.
    /// </summary>
    /// <remarks>
    /// Stated so that the next tidy-up does not quietly return the screen to a list of identical cards. The primary figure is
    /// what the collector remits; the four payor states are one block because they partition one set of people.
    /// </remarks>
    [Fact]
    public void TheSummaryStatesItsFiguresByWeightRatherThanAsOneStackOfRows()
    {
        var page = ReportPage();

        Assert.Contains("rpt-primary-value", page);
        Assert.Contains("rpt-block", page);
        Assert.Contains("rpt-grid", page);

        // The payor states sit in the block's cells, not as full-width rows of their own. Matched within the one line the button
        // is written on — deliberately not [^>]*, because the lambda in @onclick contains a ">" of its own.
        foreach (var card in new[] { "paid", "partial", "unpaid", "absent-excused" })
        {
            Assert.Matches(
                new Regex($@"class=""rpt-cell""[^\r\n]*OpenDetail\(""{Regex.Escape(card)}""\)"),
                page);
        }
    }
}
