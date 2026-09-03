namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Every facility's reports page can print its two documents, the same way, and never prints the app around them.
/// </summary>
/// <remarks>
/// The Status Report and the History are documents: each renders a <c>.print-report-sheet</c> carrying the office's own
/// letterhead, seal and prepared-by block. Until now only the market's utility statements had a way to put one on paper, so an
/// office that wanted its status report had to photograph the screen.
///
/// <para>Three things have to hold together for a printed sheet to be worth issuing, and all three are easy to get wrong one page
/// at a time: the button must exist, it must appear only on the tabs that ARE documents, and it must sit inside chrome the print
/// rules hide - otherwise the button prints itself onto the document. This test states all three across the nine pages.</para>
///
/// <para>It reads the .razor and .css files as text. Blunt, but rendering one of these pages needs three API clients, the facility
/// catalogue and JS interop stubbed, and no component test does that today. Blunt and enforced beats elegant and absent.</para>
/// </remarks>
public class FacilityReportsCanPrintTheirDocumentsTests
{
    /// <summary>Every facility's reports page. One per facility the platform bills, plus the custom-facility page.</summary>
    private static readonly string[] ReportPages =
    [
        "NpmReports.razor",
        "TccReports.razor",
        "NccReports.razor",
        "BbqReports.razor",
        "IceReports.razor",
        "SlhReports.razor",
        "TrmReports.razor",
        "TpmReports.razor",
        "CustomReports.razor",
    ];

    /// <summary>Each page offers to print.</summary>
    [Fact]
    public void EveryFacilityReportsPageHasAPrintButton()
    {
        var missing = ReadPages()
            .Where(p => !p.Text.Contains("@onclick=\"PrintPage\""))
            .Select(p => p.Page)
            .ToList();

        Assert.True(missing.Count == 0,
            "These reports pages cannot be printed at all, so the office has to photograph the screen:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// Print is offered on the document tabs only.
    /// </summary>
    /// <remarks>
    /// The Weekly, Monthly and Yearly tabs are charts and cards for reading on a screen. Printing one would put the municipality's
    /// seal and "prepared by" on what is really a screenshot, which is worse than not printing it: it looks like an issued
    /// document. So the button is gated on the two tabs that render a sheet.
    /// </remarks>
    [Fact]
    public void PrintIsOfferedOnlyOnTheTabsThatAreDocuments()
    {
        var offenders = new List<string>();

        foreach (var (page, text) in ReadPages())
        {
            if (!text.Contains("@onclick=\"PrintPage\""))
                continue;

            // The market also prints its utility statements, from a button beside that list. Every page must gate at least one
            // print button on the document tabs.
            if (!text.Contains("if (ShowPrintReport || ShowHistory)"))
                offenders.Add(page);
        }

        Assert.True(offenders.Count == 0,
            "These pages offer Print without gating it on the Status Report / History tabs, so a chart can be printed under the "
            + "office's letterhead:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The button must not print itself.
    /// </summary>
    /// <remarks>
    /// It lives in <c>.rpt-topbar-right</c>, which each page's stylesheet hides inside <c>@media print</c> along with the tabs,
    /// the date navigation and the back arrow. A page that stopped hiding that bar would put a grey "Print" button on an official
    /// document, and nobody would notice until an office filed one.
    /// </remarks>
    [Fact]
    public void ThePrintButtonSitsInChromeThePrintRulesHide()
    {
        var offenders = new List<string>();

        foreach (var (page, text) in ReadPages())
        {
            if (!text.Contains("@onclick=\"PrintPage\""))
                continue;

            var stylesheet = Path.Combine(PagesDirectory(), page + ".css");
            if (!File.Exists(stylesheet))
            {
                offenders.Add($"{page} (no stylesheet)");
                continue;
            }

            var css = File.ReadAllText(stylesheet);
            var printBlock = css.IndexOf("@media print", StringComparison.Ordinal);

            if (printBlock < 0 || !css[printBlock..].Contains("rpt-topbar-right"))
                offenders.Add(page);
        }

        Assert.True(offenders.Count == 0,
            "These pages do not hide the top bar when printing, so the Print button would appear on the printed document:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The sheet supplies its own paper margin.
    /// </summary>
    /// <remarks>
    /// The page box is deliberately margin-free - <c>print.css</c> owns it and sets <c>margin: 0</c>, so the browser has no margin
    /// box to draw its automatic date, URL and page numbers into. That only produces a readable document if the SHEET pads itself;
    /// without the padding the text prints hard against the paper edge, which is how this was first found.
    /// </remarks>
    [Fact]
    public void EverySheetPadsItselfBecauseThePageBoxHasNoMargin()
    {
        var offenders = new List<string>();

        foreach (var (page, _) in ReadPages())
        {
            var stylesheet = Path.Combine(PagesDirectory(), page + ".css");
            if (!File.Exists(stylesheet))
                continue;

            var css = File.ReadAllText(stylesheet);
            var printBlock = css.IndexOf("@media print", StringComparison.Ordinal);

            if (printBlock < 0 || !css[printBlock..].Contains("12mm"))
                offenders.Add(page);
        }

        Assert.True(offenders.Count == 0,
            "These pages print their sheet with no paper margin of its own, so the document runs to the edge:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every page this test names must exist.
    /// </summary>
    /// <remarks>
    /// A dead entry would make the rules above pass by reading nothing, which is the failure mode of every list-of-names test.
    /// </remarks>
    [Fact]
    public void EveryPageNamedHereStillExists()
    {
        var missing = ReportPages
            .Where(page => !File.Exists(Path.Combine(PagesDirectory(), page)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These pages are named by this test but no longer exist, so its rules cover nothing:\n  "
            + string.Join("\n  ", missing));
    }

    private static IEnumerable<(string Page, string Text)> ReadPages()
    {
        var dir = PagesDirectory();

        foreach (var page in ReportPages)
        {
            var path = Path.Combine(dir, page);
            if (File.Exists(path))
                yield return (page, File.ReadAllText(path));
        }
    }

    private static string PagesDirectory() => Path.Combine(
        RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Reports");

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
