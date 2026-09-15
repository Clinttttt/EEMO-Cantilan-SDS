using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Two things the Financial Report says about itself that must stay true of what it does.
///
/// <para>
/// Checked against the source rather than by rendering the page. Both are statements the markup makes about behaviour
/// defined elsewhere — in the print stylesheet, and in a domain constant — so what matters is that the two agree, which a
/// render of either one alone cannot show.
/// </para>
/// </summary>
public class FinancialReportClaimsTests
{
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("EEMOCantilanSDS.slnx").Any())
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string ReadReport(string extension) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName,
            "EEMOCantilanSDS.Client", "Components", "Pages", "Menus", $"Report.razor{extension}"));

    [Fact]
    public void TheExportButtonSaysItPrintsASummary_BecauseTwoSectionsAreLeftOut()
    {
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        // The print stylesheet keeps only the cards marked pdf-include …
        Assert.Contains(".content-area > .section-card:not(.pdf-include)", css);

        // … and neither the Attention section nor Recent Collection Records is marked, so neither is printed. If either
        // ever gains pdf-include, the button is exporting more than a summary and this should be revisited.
        // Matched on the whole opening tag rather than assuming class comes first: adding an id attribute ahead of it
        // silently stopped an earlier version of this assertion from finding the section at all.
        var attention = Regex.Match(markup, @"<div[^>]*rpt-attention-wrap[^>]*>");
        Assert.True(attention.Success, "expected the Attention section to still carry rpt-attention-wrap");
        Assert.DoesNotContain("pdf-include", attention.Value);

        Assert.Contains("Export Summary PDF", markup);
        Assert.DoesNotContain(">Export PDF<", markup);
    }

    [Fact]
    public void TheAttentionNotesStateTheOfficesThreshold_RatherThanANumberTypedIntoTheMarkup()
    {
        var markup = ReadReport(string.Empty);

        // "3 or more unpaid months" and "1–2 unpaid months" were typed literally, so changing the office's threshold left
        // the note contradicting the figure beside it. Both now read DomainRules.DelinquentThresholdMonths.
        Assert.Contains("@DomainRules.DelinquentThresholdMonths or more unpaid months", markup);
        Assert.Contains("1–@(DomainRules.DelinquentThresholdMonths - 1) unpaid months", markup);

        Assert.DoesNotContain(">3 or more unpaid months<", markup);
        Assert.DoesNotContain(">1–2 unpaid months<", markup);
    }

    [Fact]
    public void EverySectionTabMarksASectionThatExists()
    {
        // The tabs show one section at a time. A tab whose key no section carries would simply hide everything, so the two
        // lists are checked against each other rather than separately.
        var markup = ReadReport(string.Empty);

        var keys = Regex.Matches(markup, @"\(""(?<key>[a-z]+)"", ""[^""]+""\),")
            .Cast<Match>()
            .Select(m => m.Groups["key"].Value)
            .ToList();

        Assert.Equal(5, keys.Count);

        foreach (var key in keys)
            Assert.Contains($@"SectionOff(""{key}"")", markup);
    }

    [Fact]
    public void HidingASection_IsScreenOnly_SoTheExportCannotDependOnTheOpenTab()
    {
        // The whole reason the sections stay in the markup. Export Summary PDF is assembled by the print stylesheet from
        // the entire page; if rpt-sec-off hid sections in print too, the exported report would contain only whatever the
        // office happened to be looking at — an empty summary if that was Records.
        var css = ReadReport(".css");

        var offRule = Regex.Match(css, @"@media screen\s*\{[^}]*\.rpt-sec-off\s*\{[^}]*\}", RegexOptions.Singleline);
        Assert.True(offRule.Success, "rpt-sec-off must be hidden inside @media screen, never unconditionally");

        // And nothing outside that block may hide it, which would defeat the point.
        var all = Regex.Matches(css, @"\.rpt-sec-off\s*\{").Count;
        Assert.Equal(1, all);
    }

    [Fact]
    public void TheDelinquentEmptyState_PointsAtTheMoney_AsAFigureRatherThanProse()
    {
        // "No delinquent accounts" read as "nothing is badly behind" while former occupancies owed ₱11,370 in the block
        // below. The lists count only accounts still being billed, and a debt must not be stated twice on one page, so the
        // empty state points at the other figure instead of absorbing it.
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        // Stated the way the rest of the page states money — the amount as its own element, and a link to the register that
        // holds it — rather than a sentence with a peso figure inside it.
        Assert.Contains("rpt-empty-note-val\">@Money(Model.ClosedWithBalanceOutstanding)", markup);
        Assert.Contains(@"class=""rpt-empty-note"" href=""/reports/closed-accounts?status=ended""", markup);

        // Borrowed from the card below rather than invented: same border, same tinted panel, same red figure.
        Assert.Contains(".rpt-empty-note {", css);
        Assert.Contains("color: var(--red);", css);
    }

    [Fact]
    public void TheRegisterShowsTheStallAndWhoRecordedIt_BecauseBothWereAlreadyOnTheRow()
    {
        // Two things this report held and did not show. The column has always been headed "Payor / Stall" while the cell
        // rendered the payor alone, and the transaction feed has carried RecordedBy since it was normalised while the
        // report mapped it to null. For an LGU register the officer answerable for an entry is exactly what is worth
        // keeping, and three of Cantilan's market sections each hold a "Stall 1", so the payor's name does not identify
        // what was collected on.
        var markup = ReadReport(string.Empty);

        Assert.Contains("SpaceNumber.Describe(tx.StallNo)", markup);
        Assert.Contains(@"<th scope=""col"">Recorded by</th>", markup);
        Assert.Contains("tx.Collector", markup);
    }

    [Fact]
    public void TheFacilityTableReadsAsCoverage_NotABareCount()
    {
        // The expected count was computed per facility and thrown away, so the office could see that 284 records were paid
        // but not whether the roll was 290 or 400. A paid-on-service facility keeps a plain count: every transaction IS
        // the collection, so a denominator would have to be invented for it.
        var markup = ReadReport(string.Empty);

        Assert.Contains("f.ExpectedRecords", markup);
        Assert.Contains("f.PaidOnService || f.ExpectedRecords <= 0", markup);
        Assert.Contains(@"<th scope=""col"">Paid / expected</th>", markup);
    }

    [Fact]
    public void TheHeadlineFiguresAreTheFourThatReconcile_AndThereAreStillFourOfThem()
    {
        // Billed leads because the other three answer to it: Billed = Collected + Current-period unpaid. It used to appear
        // only as sub-text under the collection rate, while the fourth card held a record COUNT — operational information
        // given the same weight as money.
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        var overview = markup.Substring(markup.IndexOf(@"id=""rpt-overview"""));
        overview = overview.Substring(0, overview.IndexOf("rpt-activity"));

        var values = Regex.Matches(overview, @"kpi-value"">(?<v>[^<]+)<")
            .Cast<Match>()
            .Select(m => m.Groups["v"].Value.Trim())
            .ToList();

        // Four, in the order a statement of position reads. Four matters: the grid is four columns and print forces four,
        // so a fifth card would silently reflow the exported document.
        Assert.Equal(
            new[] { "@Money(Model.Billed)", "@Money(Model.Collected)", "@Money(Model.CurrentPeriodUnpaid)", "@Model.CollectionRatePct%" },
            values);
        Assert.Contains("grid-template-columns: repeat(4, 1fr) !important", css);

        // The record count was demoted, not dropped, and its strip prints with the KPIs and hides with the tab.
        Assert.Contains(@"class=""rpt-activity pdf-include@(SectionOff(""overview""))""", markup);
        Assert.Contains("Model.PaidRecords", markup);
        Assert.Contains("Model.ExpectedRecords", markup);
    }

    [Fact]
    public void TheRegistersRowCap_IsTheSameNumberOnThePageAsOnTheServer()
    {
        // The page says either "the 50 most recent of this period" or "N transactions", and it decides which by comparing
        // what it received against its own copy of the cap. If the two ever part, the page would claim a complete register
        // while showing a truncated one — on a financial report that is worse than saying nothing at all.
        var markup = ReadReport(string.Empty);

        var pageLimit = Regex.Match(markup, @"RegisterListLimit = (?<n>\d+);");
        Assert.True(pageLimit.Success, "expected the page to state its register cap");

        var handler = File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName,
            "EEMOCantilanSDS.Application", "Queries", "Reports", "GetFinancialReport",
            "GetFinancialReportQueryHandler.cs"));

        var serverLimit = Regex.Match(handler, @"RegisterLimit = (?<n>\d+);");
        Assert.True(serverLimit.Success, "expected the handler to state its register cap");

        Assert.Equal(serverLimit.Groups["n"].Value, pageLimit.Groups["n"].Value);
    }

    [Fact]
    public void TheRegisterIsTitledForThePeriod_AndPointsAtTheFullLedger()
    {
        // It used to read "Recent Collection Records · Latest recorded payments", which is activity for the scope. The
        // section is the evidence behind the period's totals, so it says which period — and hands off to the transactions
        // page rather than growing a page control the feed cannot support correctly.
        var markup = ReadReport(string.Empty);

        Assert.Contains("Collection register — @PeriodLabel", markup);
        Assert.DoesNotContain("Recent Collection Records</div>", markup);
        Assert.Contains(@"href=""/transactions""", markup);
    }

    [Fact]
    public void TheRegisterPrintsAsItsOwnDocument_WithoutChangingTheSummaryExport()
    {
        // Two documents from one page. The summary is what the office signs off, so the register was NOT added to it —
        // fifty rows a period would make it a different document. The register prints under a class the print helper adds
        // for the duration of the print.
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        Assert.Contains(@"id=""rpt-sheet""", markup);
        Assert.Contains(@"stalltrackPrint.withClass"", ""#rpt-sheet"", ""print-register""", markup);

        // Register mode hides the summary's own sections and shows the register …
        Assert.Contains(".print-register #rpt-records { display: block !important; }", css);
        Assert.Contains(".print-register .kpi-row,", css);

        // … and the register still carries no pdf-include, which is what leaves the Summary PDF exactly as it was.
        var records = Regex.Match(markup, @"<div id=""rpt-records""[^>]*>");
        Assert.True(records.Success, "expected the register section to still carry its id");
        Assert.DoesNotContain("pdf-include", records.Value);
    }

    [Fact]
    public void RegisterModeReliesOnAnId_BecauseTheSummaryRuleWouldOtherwiseOutrankIt()
    {
        // The summary print rule hides every section-card without pdf-include, and the register is one of them. A class
        // alone could not bring it back — three classes beat two — so the id in these selectors is load-bearing rather
        // than stylistic, and a future tidy-up that swaps it for a class would silently print an empty register.
        var css = ReadReport(".css");

        Assert.Contains(".content-area > .section-card:not(.pdf-include) { display: none !important; }", css);
        Assert.Matches(@"\.print-register #rpt-records\s*\{[^}]*display:\s*block", css);
    }
}
