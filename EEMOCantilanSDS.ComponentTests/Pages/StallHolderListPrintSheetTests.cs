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

    /// <summary>
    /// The two downloads are ONE control with two working halves.
    /// </summary>
    /// <remarks>
    /// The office asked for Excel and CSV to stop reading as two separate choices: it is the same list either way. Merging them is
    /// only safe if each half keeps its own action, so that is what is asserted — Excel remains a button that calls DownloadExcel,
    /// CSV remains a download link to the generated file — alongside the group that makes them look like one button.
    ///
    /// <para>Asserted against the file rather than a render because the joining is done in CSS: a collapsed shared edge cannot be
    /// observed from markup.</para>
    /// </remarks>
    [Fact]
    public void TheExportsAreOneControlButTwoSeparateActions()
    {
        var razor = ClientFile("Components", "Pages", "Reports", "StallHolderList.razor");
        var css = ClientFile("Components", "Pages", "Reports", "StallHolderList.razor.css");

        // One group holding both halves.
        Assert.Contains("class=\"sh-split\"", razor);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(razor, @"sh-split-part").Count);

        // Still two actions: a button that exports the workbook, and a link that downloads the CSV.
        Assert.Matches(@"<button[^>]*sh-split-part[^>]*@onclick=""DownloadExcel""", razor);
        Assert.Matches(@"<a[^>]*sh-split-part[^>]*download=""@CsvFileName""[^>]*href=""@CsvHref""", razor);

        // The group draws the outer corners and the halves share ONE edge, which is what makes it read as a single button.
        Assert.Matches(@"\.sh-split \.sh-split-part \{ border-radius: 0", css);
        Assert.Matches(@"\.sh-split \.sh-split-part:first-child \{[^}]*border-top-left-radius", css);
        Assert.Matches(@"\.sh-split \.sh-split-part:last-child \{[^}]*border-top-right-radius", css);
        Assert.Matches(@"\.sh-split \.sh-split-part \+ \.sh-split-part \{ margin-left: -1px", css);
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

        // The caption row it replaced is gone from the markup, not merely hidden: a hidden row that no rule shows is
        // the kind of thing a later edit revives by accident.
        var razor = ClientFile("Components", "Pages", "Reports", "StallHolderList.razor");
        Assert.DoesNotContain("sh-fac-row", razor);
    }

    [Fact]
    public void ATableTHATCONTINUESStartsBelowThePapersEdgeToo()
    {
        // Reported after the facility padding shipped: Tampak's rows broke across sheets, so the padding was drawn on
        // sheet one and sheet two opened with the column headings against the paper's edge. Only a head repeats at the
        // top of every sheet a table runs onto, so a blank first row in the head is the one thing that can land there;
        // it is cancelled on the sheet the table begins on by an equal negative margin.
        var razor = ClientFile("Components", "Pages", "Reports", "StallHolderList.razor");
        Assert.Contains(@"<tr class=""sh-gap-row"" aria-hidden=""true""><td colspan=""99""></td></tr>", razor);

        var print = RosterPrintBlock();
        Assert.Matches(@"\.sh-gap-row \{ display: table-row", print);
        Assert.Matches(@"\.sh-gap-row td \{[^}]*height: 7mm", print);
        Assert.Matches(@"\.sh-gap-row td \{[^}]*border: 0 !important", print);   // no rule drawn across the gap
        Assert.Matches(@"\.sh-table \{ margin-top: -7mm", print);                // cancelled on the first sheet

        // Hidden where there is nothing to show.
        var css = ClientFile("Components", "Pages", "Reports", "StallHolderList.razor.css");
        Assert.Matches(@"(?m)^\.sh-gap-row \{ display: none", css);
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
