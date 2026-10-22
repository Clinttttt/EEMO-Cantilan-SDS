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

        Assert.Contains("Fully elapsed unpaid months of the current year", markup);
        Assert.Contains("Previous-year rent, expired contracts and ended occupancies", markup);
        Assert.DoesNotContain("DomainRules.DelinquentThresholdMonths - 1", markup);
        Assert.DoesNotContain("unpaid months</div>", markup);
    }

    [Fact]
    public void SettingsAndFinancialSummary_DoNotPublishAnAgeBasedArrearsSplit()
    {
        var root = RepositoryRoot().FullName;
        var settings = File.ReadAllText(Path.Combine(root, "EEMOCantilanSDS.Client", "Components", "Pages", "Menus", "Settings.razor"));
        var summary = File.ReadAllText(Path.Combine(root, "EEMOCantilanSDS.Client", "Components", "Pages", "Reports", "FinancialSummaryDocument.razor"));
        var completeDocumentation = File.ReadAllText(Path.Combine(root, "docs", "business", "EEMO_BUSINESS_RULES.md"));

        // IA-069 defines Arrears per obligation (year / ended occupancy / expired contract), never by the age of an unpaid-month count.
        Assert.Contains("per obligation", settings);
        Assert.DoesNotContain("ArrearsMinMonths", settings);
        Assert.Contains("Arrears — previous-year rent, expired contracts and ended occupancies", summary);
        Assert.DoesNotContain("1 to @(DomainRules.DelinquentThresholdMonths - 1)", summary);
        Assert.DoesNotContain("3+ unpaid months inside a rolling 12-month window = delinquent; 1–2 = arrears", completeDocumentation);
    }

    [Fact]
    public void TheFollowUpDividerSpansTheFullHeightOfTheTwoColumns()
    {
        var css = ReadReport(".css");
        var grids = Regex.Matches(css, @"\.rpt-attention\s*\{(?<rules>[^}]*)\}")
            .Cast<Match>()
            .Select(match => match.Groups["rules"].Value)
            .ToList();

        Assert.Contains(grids, rules => rules.Contains("align-items: stretch", StringComparison.Ordinal));
        Assert.DoesNotContain(grids, rules => rules.Contains("align-items: start", StringComparison.Ordinal));
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

        Assert.Equal(6, keys.Count);

        // A section is either hidden by the screen-only class (it must exist for the export) or rendered only when open.
        foreach (var key in keys)
            Assert.True(
                markup.Contains($@"SectionOff(""{key}"")") || markup.Contains($@"SectionShown(""{key}"")") || markup.Contains($@"=> ""{key}"""),
                $"the '{key}' tab marks no section");
    }

    [Fact]
    public void OnlyASectionThatIsNeverPrinted_MayBeLeftOutOfTheMarkup()
    {
        // Export Summary PDF is assembled by the print stylesheet from the whole page, whatever tab is open, so every
        // pdf-include section has to exist at all times — hidden by a class, never omitted. Receivables is read on screen
        // only, so it may leave the markup while another tab is open.
        var markup = ReadReport(string.Empty);

        Assert.Contains(@"SectionShown(""followup"")", markup);
        Assert.Contains(@"SectionOff(""overview"")", markup);
        Assert.DoesNotContain(@"SectionShown(""overview"")", markup);
        Assert.Matches(@"SectionShown\(""followup""\)\s*\)\s*\{\s*<div id=""rpt-followup""", markup);

        // The physical-facility analysis is no longer on the Trends & Targets screen, but the print document still carries it:
        // it stays in the markup, pdf-include, hidden on screen only.
        Assert.Matches(@"<div id=""rpt-facility"" class=""section-card pdf-include rpt-sec-off""", markup);

        // The legacy-source register, Miscellaneous and the facility-oriented Revenue Trend were retired from the screen.
        foreach (var gone in new[] { @"id=""rpt-records""", @"id=""rpt-misc""", @"id=""rpt-trend""", "Legacy-source collections", "Physical facility analysis — @PeriodLabel</div>" })
            Assert.DoesNotContain(gone, markup.Replace("Physical facility analysis — @PeriodLabel<span", "x"));
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
        Assert.Contains("private decimal FollowUpOutstanding => Model.DelinquentOutstandingTotal", markup);
        Assert.Contains("Model.LapsedWithBalanceOutstanding", markup);
        Assert.Contains("Model.ClosedWithBalanceOutstanding", markup);

        Assert.Contains("Not to be added", markup);
        Assert.Contains("Lapsed term exposure", markup);
        Assert.Contains("within Arrears", markup);

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
    public void TheRateBasisMark_IsAvailableOnScreenAndPrintsNothing()
    {
        // The rate in this table is not the rate the Dashboard states for the same market — 30% against 26% on September
        // 2026 — because fish by the kilo and the market's utilities are collected without a monthly assessment, so they
        // sit in this table's numerator with nothing answering them in its denominator. Both figures are correct on their
        // own basis, and the office reads them side by side, so the rule is stated where the period is named.
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        // Beside the period, not in a sub-line: the rule is wanted when a figure is questioned, not on every reading.
        Assert.Contains("Physical facility analysis — @PeriodLabel<span class=\"rpt-rule-wrap no-print\">", markup);
        Assert.Contains("_facilityRuleOpen", markup);

        // Reachable by keyboard and announced: a bare glyph would state the rule to no one who needs it read out.
        Assert.Contains("aria-expanded=\"@(_facilityRuleOpen ? \"true\" : \"false\")\"", markup);
        Assert.Contains("aria-controls=\"rpt-facility-rule\"", markup);
        Assert.Contains("aria-label=\"How the rate in this table is measured\"", markup);
        Assert.Contains("id=\"rpt-facility-rule\"", markup);
        Assert.Contains(".rpt-rule-mark:focus-visible", css);

        // Hover and keyboard focus reveal it without a click, which is why it is in the markup at all times.
        Assert.Contains(".rpt-rule-wrap:hover .rpt-rule-pop", css);
        Assert.Contains(".rpt-rule-wrap:focus-within .rpt-rule-pop", css);

        // Plain terms, the scope it applies to, and the phrase that answers the question the office actually asks.
        Assert.Contains("Collected ÷ (collected + unpaid)", markup);
        Assert.Contains("@Model.FacilityCount facilities", markup);
        Assert.Contains("daily fees against daily fees due", markup);

        // The mark and its note are one screen-only element: nothing about the rule reaches paper.
        Assert.Contains("class=\"rpt-rule-wrap no-print\"", markup);
    }

    [Fact]
    public void TheFacilityTablesClosingStatement_SplitsTheTotalByModel_AndDerivesItFromTheTotals()
    {
        // The total row states one rate over two unlike things: a paid-on-service fee is in Collected but can never be in
        // Unpaid, so it lifts the rate toward 100% while the whole outstanding balance sits on the billed facilities. The
        // split says so at the rows it describes, using the same distinction the Overview panel makes.
        var markup = ReadReport(string.Empty);

        // Derived from the totals, not by summing rows independently, so the two halves always add back to the row above.
        Assert.Contains("Model.Facilities.Where(f => f.PaidOnService).Sum(f => f.Collected)", markup);
        Assert.Contains("Model.Collected - facServiceCollected", markup);

        // The billed rate is measured against billed billing only — collected plus the period's unpaid.
        Assert.Contains("facBilledCollected + Model.CurrentPeriodUnpaid", markup);

        // Both sides are named, and the service side simply states what it collected: a rate for it would be a rate over
        // nothing, and explaining that in the row was prose where a figure belongs.
        Assert.Contains("Paid on service", markup);
        Assert.DoesNotContain("nothing assessed, so nothing can be owed", markup);
    }

    [Fact]
    public void TheFacilityTablesClosingStatement_NamesWhereTheOutstandingIsConcentrated()
    {
        // The table holds this, but only after the office has read every row and done the arithmetic. It is stated as a
        // share of the period's outstanding, and only when there is something outstanding to share.
        var markup = ReadReport(string.Empty);

        Assert.Contains(".Where(f => !f.PaidOnService && (f.Unpaid ?? 0m) > 0m)", markup);
        Assert.Contains(".OrderByDescending(f => f.Unpaid ?? 0m)", markup);
        Assert.Contains("facWorst is not null && Model.CurrentPeriodUnpaid > 0m", markup);

        // Paid-on-service facilities are excluded from the search: their Unpaid is null because none can exist.
        Assert.Contains("Most of it is in one place", markup);
    }

    [Fact]
    public void ReceivableAging_AppearsOnlyWhenTheDebtSpansMoreThanOneBand()
    {
        // With every account in the youngest band the schedule repeats the delinquency count beside it, and naming the empty
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
