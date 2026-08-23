using System.IO;
using Xunit;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The printed stallholder roster, asserted against the stylesheets because a print rule cannot be observed from
/// rendered markup: the browser only applies it while paginating. Each fact here was a defect the office reported
/// from an actual print, so each is pinned rather than left to the next person's judgement.
/// </summary>
public class StallHolderListPrintSheetTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ClientFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot(), "EEMOCantilanSDS.Client" }.Concat(parts).ToArray()));

    private static string RosterPrintBlock()
    {
        var css = ClientFile("Components", "Pages", "Reports", "StallHolderList.razor.css");
        var at = css.IndexOf("@media print", StringComparison.Ordinal);
        Assert.True(at >= 0, "the roster has no print block at all");
        return css[at..];
    }

    [Fact]
    public void TheFacilityIsNamedONCEAboveItsSections()
    {
        // The fault: the facility name was a row inside every section's table, so the market printed
        // "VEGETABLE AREA" and then "NEW PUBLIC MARKET" under it, then repeated that name over the Sari Sari table
        // and every other section. The name belongs above the section bands, stated once, the way the export sheet
        // reads it.
        var print = RosterPrintBlock();

        Assert.Matches(@"\.sh-fac-head \{[^}]*display: block", print);
        Assert.Matches(@"\.sh-fac-row \{ display: none", print);
    }

    [Fact]
    public void ThatHeadingCarriesNoScreenFurnitureAndNoFILL()
    {
        // The label's model tag, active count and monthly figure are all restated by the section band and by the
        // table's own Total row, so on paper they are noise. Its surface is the paper's white: a grey band read as a
        // second section heading, and grey costs ink on a document that is filed.
        var print = RosterPrintBlock();

        Assert.Matches(@"\.sh-fac-head \.sh-fac-stats, \.sh-fac-head \.sh-tag \{ display: none", print);
        Assert.Matches(@"\.sh-fac-head \{[^}]*background: #fff", print);
    }

    [Fact]
    public void PaperIsWHITERightToItsEdges()
    {
        // Reported as "the outer whitespace is not pure white". The shell paints itself --bg (#f0f4f8) so sheets read
        // as sheets on screen; with background graphics on - which every report needs, for the seal and the table
        // headings - that tint printed across the whole page. Fixed in print.css, which loads last, so it holds for
        // every printable page and not just this one.
        var print = ClientFile("wwwroot", "css", "print.css");

        Assert.Contains("@media print", print);
        Assert.Matches(@"html, body \{ background: #fff !important", print);
    }

    [Fact]
    public void TheShellsFullHeightFloorIsLIFTEDOnPaper()
    {
        // The blank second sheet. .admin-layout is a full-height flex column on screen (min-height: 100vh); on paper
        // a viewport height IS a page height, so that floor kept the layout box a whole page tall even when the sheet
        // inside it ended halfway down, and the leftover height printed as an empty sheet.
        var app = ClientFile("wwwroot", "app.css");
        var at = app.IndexOf("@media print", StringComparison.Ordinal);
        Assert.True(at >= 0);

        var print = app[at..];
        Assert.Matches(@"\.admin-layout \{[^}]*min-height: 0 !important", print);
    }

    [Fact]
    public void ASheetTHATCONTINUESStillStartsBelowThePapersEdge()
    {
        // Reported on sheet two of the roster: the facility that carried over sat hard against the top edge. The gap
        // has to be PADDING - a margin at the top of a printed page is dropped by the browser - and it is stated once,
        // on the facility, so the first table on sheet one sits exactly as far down as the first table on sheet two.
        // A top page margin would have been the obvious fix and is the wrong one: @page margin is 0 on purpose, so the
        // browser has no margin box to draw its date, URL and page number into.
        var print = RosterPrintBlock();

        Assert.Matches(@"\.sh-facility \{[^}]*padding-top: 7mm", print);
        Assert.Matches(@"\.sh-rpt-head \{ margin-bottom: 0", print);
        Assert.DoesNotMatch(@"@page[^{]*\{[^}]*margin:\s*(?!0)", print);
    }

    [Fact]
    public void ATotalMayNotBeginASheetOnItsOwn()
    {
        // Tampak's rows fitted on sheet one but its Total did not, so sheet two opened with a repeated heading row and
        // a single figure - a page stating a total for rows nobody can see on it.
        var print = RosterPrintBlock();

        Assert.Matches(@"\.sh-table tbody tr:last-child \{ break-after: avoid", print);
        Assert.Matches(@"\.sh-table tfoot \{ break-before: avoid", print);
    }

    [Fact]
    public void ATotalPrintsOncePerFacilityAndHeadingsRepeat()
    {
        // Guarding the two rules a later edit is most likely to undo: a tfoot repeats on every page by default, so a
        // facility running onto a second sheet printed its Total twice, which on a filed document reads as two
        // different figures; the column headings must repeat, because rows without headings cannot be read.
        var print = RosterPrintBlock();

        Assert.Matches(@"\.sh-table tfoot \{ display: table-row-group", print);
        Assert.Matches(@"\.sh-table thead \{ display: table-header-group", print);
    }
}
