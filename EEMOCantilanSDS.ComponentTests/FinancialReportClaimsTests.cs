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

    private static string ReadPrintJs() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName, "EEMOCantilanSDS.Client", "wwwroot", "js", "print.js"));

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
        overview = overview.Substring(0, overview.IndexOf("rpt-panels"));

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

        // The record count was demoted from the KPI row, not dropped: it is context in the composition panel's footer, and
        // the panels print with the KPIs and hide with the tab.
        Assert.Contains(@"class=""rpt-panels pdf-include@(SectionOff(""overview""))""", markup);
        Assert.Contains("Model.PaidRecords", markup);
        Assert.Contains("Model.ExpectedRecords", markup);
    }

    [Fact]
    public void CollectionByModel_IsDerivedFromTheHeadlineFigure_SoTheTwoPartsAlwaysAddBackToIt()
    {
        // Composition must reconcile with the card above it. Summing every facility row independently would let a rounding
        // or grouping difference put a split on screen that disagrees with Collected, so the service half is summed and the
        // recurring half is what remains of the headline figure.
        var markup = ReadReport(string.Empty);

        Assert.Contains("Model.Facilities.Where(f => f.PaidOnService).Sum(f => f.Collected)", markup);
        Assert.Contains("Model.Collected - serviceCollected", markup);

        // The distinction is the system's own, not an invented category.
        Assert.Contains("Paid on service", markup);
        Assert.Contains("Recurring", markup);
    }

    [Fact]
    public void TheOutstandingPanel_StatesScopesAndMarksThePartThatIsNotAnAddend()
    {
        // Four figures at three different scopes. The lapsed line is a SLICE of the whole-account line, so it is worded and
        // indented as one — the confusion this panel exists to end is two figures being added that describe the same debt.
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        Assert.Contains("Model.CurrentPeriodUnpaid", markup);
        Assert.Contains("Model.DelinquentOutstandingTotal + Model.ArrearsOutstandingTotal", markup);
        Assert.Contains("Model.LapsedWithBalanceOutstanding", markup);
        Assert.Contains("Model.ClosedWithBalanceOutstanding", markup);

        Assert.Contains("Not to be added", markup);
        Assert.Contains("Lapsed term exposure", markup);
        Assert.Contains("within whole account", markup);

        // Every label starts on the same line: the relationship is carried by the wording, not by an indent that leaves
        // one row out of alignment with the others.
        Assert.DoesNotContain(".rpt-pos-row.is-part {", css);
    }

    [Fact]
    public void TheRecurringRate_IsBuiltFromRecurringBillingAlone_AndOnlyShownWhenItDiffers()
    {
        // The headline rate is Collected / Billed, and a paid-on-service fee sits in both halves while never being capable
        // of arrears — so it pulls the rate toward 100% and the figure stops describing rent collection. The qualified rate
        // divides recurring collections by recurring billings only.
        var markup = ReadReport(string.Empty);

        // Recurring billed = recurring collected + the period's unpaid, because only recurring billing can be unpaid.
        Assert.Contains("recurringCollected + Model.CurrentPeriodUnpaid", markup);
        Assert.Contains("recurringCollected / recurringBilled * 100m", markup);

        // Shown only when it says something the headline does not, so it never just repeats the card above it.
        Assert.Contains("serviceCollected > 0m && recurringBilled > 0m && recurringRate != Model.CollectionRatePct", markup);
    }

    [Fact]
    public void ReceivableAging_AppearsOnlyWhenTheDebtSpansMoreThanOneBand()
    {
        // With every account in the youngest band the schedule repeats the arrears count beside it, and naming the empty
        // bands states an absence the office already knows — the ages are a scale for reading a schedule, not a prediction
        // that anyone will reach them. So the panel appears when there is a spread, and stays away when there is not.
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        Assert.Contains("Model.Aging.Count(b => b.Accounts > 0) > 1", markup);
        Assert.DoesNotContain("Nothing owing at", markup);

        // A lapsed term still wants renewing, so that one figure survives the panel being withheld.
        Assert.Contains("rpt-aging-lean", markup);

        // The column count follows the surviving bands, so two filled bands do not leave two empty columns behind.
        Assert.Contains("--rpt-aging-count: @filled.Count", markup);
        Assert.Contains("repeat(var(--rpt-aging-count, 4), minmax(0, 1fr))", css);
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
        Assert.Contains(@"stalltrackPrint.sectionDocument"", ""#rpt-sheet"", ""print-register""", markup);

        // Register mode hides the summary's own sections and shows the register …
        Assert.Contains(".print-register #rpt-records { display: block !important; }", css);
        Assert.Contains(".print-register .kpi-row,", css);

        // … the spacer that forces a page break for the facility card goes with it, or the register begins on a blank
        // sheet, which is exactly what it did …
        Assert.Contains(".print-register .rpt-page-space { display: none !important; }", css);

        // … and the margin is ZERO, so Chromium has no margin box to print its date, title and URL into. The whitespace
        // comes from the container's own print padding and from the repeated header and footer rows.
        Assert.Contains("margin: 0;", ReadPrintJs());
        Assert.Contains(".content-area { page: report-doc; padding: 10mm 12mm !important;", css);

        // The card chrome is stripped for print: a bordered panel broken across sheets draws its background to the paper's
        // edge on every page between its true start and end, which read as the table falling off the sheet.
        Assert.Contains(".print-register #rpt-records {", css);
        Assert.Contains("background: transparent !important;", css);

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
