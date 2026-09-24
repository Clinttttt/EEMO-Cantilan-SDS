using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using ReportPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Report;

/// <summary>
/// bUnit render tests for the Financial Reports page. They verify the page binds the
/// <see cref="FinancialReportDto"/> from the API client into the KPIs, facility table, trend bars,
/// and the expandable NPM detail — complementing the handler unit tests (which cover the figures).
/// </summary>
public class ReportPageTests : TestContext
{
    /// <summary>
    /// bUnit's default WaitForAssertion timeout is 1 second. The page renders its KPIs, table and trend bars
    /// only after an async API load completes, so on a loaded machine (both test assemblies running at once)
    /// 1s can elapse before the second render — a false failure. An explicit, generous timeout makes these
    /// render assertions deterministic without slowing the passing path (they return as soon as they pass).
    /// </summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);
    private static FinancialReportDto SampleReport() => new(
        PeriodLabel: "March 2026",
        ScopeLabel: "All facilities",
        Frequency: "Monthly",
        FacilityCount: 8,
        Collected: 242_170m,
        CurrentPeriodUnpaid: 57_400m,
        Billed: 299_570m,
        CollectionRatePct: 81,
        PaidRecords: 168,
        ExpectedRecords: 210,
        CollectedPreviousPeriod: null,
        PreviousPeriodLabel: "February 2026",
        Delinquent: new List<AttentionAccountDto>
        {
            new("Rosa Magbanua", FacilityCode.TCC, "04", "TCC · Stall 04", 4_800m, 3),
            new("Merlita A. Abuso", FacilityCode.ICE, "7", "ICE · Stall 7", 33_300m, 37, TermLapsed: true),
            new("Jose Dalumpines", FacilityCode.NCC, "11-B", "NCC · Stall 11-B", 3_600m, 2)
        },
        Arrears: null,
        // The three delinquent accounts above are aged separately: Jose at 2 months, Rosa at 3, Merlita at 37.
        Aging: new List<ReceivableAgingBandDto>
        {
            new("1–2 months", 1, 3_600m),
            new("3–5 months", 1, 4_800m),
            new("6–11 months", 0, 0m),
            new("12+ months", 1, 33_300m)
        },
        // Merlita's term has run out while she remains in the space; her ₱33,300 is already inside the delinquent figure.
        LapsedWithBalanceCount: 1,
        LapsedWithBalanceOutstanding: 33_300m,
        Trend: new List<ReportTrendPointDto>
        {
            new("Feb 2026", 2026, 2, 233_800m, 72_120m, false),
            new("Mar 2026", 2026, 3, 242_170m, 57_400m, true)
        },
        YtdCollected: 475_970m,
        Facilities: new List<FinancialFacilityRowDto>
        {
            new(FacilityCode.NPM, "New Public Market", "Daily stall", false, 2_242m, 1_410m, 4, 6, 55, "Behind",
                new NpmFacilityDetailDto(1_710m, 532m, 532m, 1_410m, 3_600m, 1_890m,
                    ElecCollected: 320m, WaterCollected: 180m, UtilityOutstanding: 90m)),
            new(FacilityCode.TRM, "Transport Terminal", "Per-trip", true, 300m, null, 10, 0, 100, "Paid on service")
        },
        RecentRecords: new List<FinancialRecordDto>
        {
            new("OR-9", "Luz Cano", FacilityCode.NPM, "5", new DateTime(2026, 3, 25), null, "Daily Fee", 930m)
        },
        // Set to agree with the delinquent list above: below the display cap, the totals and list describe the same
        // accounts. Left at their defaults these would be nought, and the page header states them.
        DelinquentAccountsTotal: 3,
        DelinquentOutstandingTotal: 41_700m);   // 4,800 + 33,300 + 3,600

    private IRenderedComponent<ReportPage> RenderReport(FinancialReportDto dto)
    {
        var api = new Mock<IReportsApiClient>();
        api.Setup(a => a.GetFinancialReportAsync(
                It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<FacilityCode?>()))
            .ReturnsAsync(Result<FinancialReportDto>.Success(dto));

        Services.AddSingleton(api.Object);
        // The global _Imports.razor injects these into every component; stub them so the page resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        // BrandingState (via _Imports) and FacilityState (page-injected) — register with stub API clients;
        // both fall back gracefully (Cantilan defaults / all facilities) so no data is needed to render.
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Head");

        return RenderComponent<ReportPage>();
    }

    /// <summary>
    /// Opens a section the way the office does. Attention and the register are in the markup only while their own tab is
    /// open — neither is ever printed, so neither has to exist otherwise — which means a test that reads them has to
    /// click first. It also makes the negative assertions honest: "no capped note" now means the note is absent from a
    /// section that is actually on screen, rather than from a section that was not rendered at all.
    /// </summary>
    private static void OpenSection(IRenderedComponent<ReportPage> cut, string label)
    {
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".rpt-sec-tab")), RenderTimeout);
        cut.FindAll(".rpt-sec-tab").Single(t => t.TextContent.Trim() == label).Click();
    }

    [Fact]
    public void Renders_Kpis_From_Api()
    {
        var cut = RenderReport(SampleReport());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("₱242,170", cut.Markup);                       // Collected KPI
            Assert.Contains("Current-period unpaid balance", cut.Markup);
            Assert.Contains("₱57,400", cut.Markup);                        // Unpaid KPI
            Assert.Contains("81%", cut.Markup);                            // Collection rate
            // Billed leads the row now, and the record count moved to the activity strip below it: operational information
            // beside three figures that are money. Both of its numbers are still stated, just not as a headline card.
            Assert.Contains("Assessed", cut.Markup);                       // Billed KPI
            Assert.Contains("₱299,570", cut.Markup);                       // 242,170 collected + 57,400 unpaid
            // The record count is context in the composition panel's footer now, not a headline card. Both numbers are
            // still stated, and the panel reads them as coverage: "168 of 210 expected records".
            Assert.Contains("expected records recorded", cut.Markup);
            Assert.Contains("210", cut.Markup);

            // Collection by model splits the same Collected figure into recurring rent and paid-on-service fees.
            Assert.Contains("Paid on service", cut.Markup);
            Assert.Contains("Recurring", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public void Renders_FacilityRows_And_TrendBars()
    {
        var cut = RenderReport(SampleReport());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("New Public Market", cut.Markup);
            Assert.Contains("Transport Terminal", cut.Markup);
            Assert.Contains("Paid on service", cut.Markup);
            // Two trend points → two bar groups.
            Assert.Equal(2, cut.FindAll(".bar-group").Count);
        }, RenderTimeout);
    }

    [Fact]
    public void TheTotalsLine_StatesMovementBesideTheTotal_WithTheFiguresKeptApart()
    {
        // Movement used to be a panel of three figures under the table, two of which the table already listed as rows.
        // It now rides on the totals line. Razor strips markup whitespace at the edge of a code block, which once ran
        // "₱6,128" straight into "69.0% of August 2026" on screen, so the separation is asserted here as the page reads
        // it rather than as CSS that a stylesheet change could quietly drop.
        var cut = RenderReport(SampleReport() with
        {
            CollectedPreviousPeriod = 233_800m,          // February's collected, the bar before the selected one
            PreviousPeriodLabel = "February 2026"
        });

        cut.WaitForAssertion(() =>
        {
            // Non-breaking spaces separate the parts, so compare on ordinary ones.
            var note = cut.Find(".rpt-ytd-note").TextContent.Replace('\u00A0', ' ');

            // 242,170 against 233,800 is a rise of 8,370, which is 3.6% of the month before. Every figure on the line is
            // a separate word: this is the assertion that would have caught the run-together text.
            Assert.Contains("₱475,970 ↑ ₱8,370 (3.6% of February 2026)", note);

            // The period figures themselves are the table's job, not this line's.
            Assert.DoesNotContain("MOVEMENT", cut.Markup);
            Assert.Empty(cut.FindAll(".rpt-move-close"));
        }, RenderTimeout);
    }

    [Fact]
    public void Miscellaneous_StatesTheChargesPerPayor_AndThatTheyAreNotAnAddition()
    {
        // The section's whole purpose: what the readings charged, from whom, and — in one line — that the money is
        // already inside the period's collected. A reader who added it on would double the office's own revenue.
        var cut = RenderReport(SampleReport() with
        {
            Misc = new FinancialMiscDto(
                Charged: 919m, Collected: 700m, Due: 219m, Settled: 2, Outstanding: 2, AmountsRecorded: true,
                Rows: new List<MonthEndUtilityRowDto>
                {
                    new("01", "Pedro Santos", 219m, 100m, 100m, 0m, "OR-2", FacilityCode.NPM, PaymentStatus.Partial, PaymentStatus.Unpaid),
                    new("07", "Maria Velasco", 400m, 400m, 200m, 200m, "OR-1", FacilityCode.NPM, PaymentStatus.Paid, PaymentStatus.Paid)
                })
        });

        OpenSection(cut, "Misc");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#rpt-misc")), RenderTimeout);

        var section = cut.Find("#rpt-misc").TextContent.Replace('\u00A0', ' ');

        Assert.Contains("Maria Velasco", section);
        Assert.Contains("OR-2", section);
        Assert.Contains("₱919", section);                                   // charged, the figure no other section states

        // A label and its figures, not a sentence — the sentence ran "₱0 0 settled" together and off the card.
        var close = cut.Find(".rpt-misc-close");
        Assert.Contains("Already inside the period's collected", close.TextContent);
        Assert.Equal("₱700", close.QuerySelector("strong")!.TextContent.Trim());
        Assert.Contains("2 settled", close.TextContent);
        Assert.Contains("2 still owing", close.TextContent);

        // One facility, so no facility column: the report names a place only when the rows come from more than one.
        Assert.DoesNotContain("Facility", cut.Find("#rpt-misc thead").TextContent);
    }

    [Fact]
    public void Miscellaneous_StatesStandingsInsteadOfMoney_WhenTheOfficeRecordsNoAmounts()
    {
        // The shape the coming settings change will select: an office that only marks a utility settled or not has no
        // money to report, and a table of noughts would read as "nothing charged". The same table states standings, and
        // the money columns are not offered at all.
        var cut = RenderReport(SampleReport() with
        {
            Misc = new FinancialMiscDto(
                Charged: 0m, Collected: 0m, Due: 0m, Settled: 1, Outstanding: 2, AmountsRecorded: false,
                Rows: new List<MonthEndUtilityRowDto>
                {
                    new("01", "Pedro Santos", 0m, 0m, 0m, 0m, null, FacilityCode.NPM, PaymentStatus.Paid, PaymentStatus.Unpaid),
                    new("07", "Maria Velasco", 0m, 0m, 0m, 0m, null, FacilityCode.NPM, PaymentStatus.Partial, PaymentStatus.Unpaid)
                })
        });

        OpenSection(cut, "Misc");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#rpt-misc")), RenderTimeout);

        var head = cut.Find("#rpt-misc thead").TextContent;
        Assert.Contains("Electricity", head);
        Assert.DoesNotContain("Charged", head);
        Assert.DoesNotContain("Balance", head);

        var body = cut.Find("#rpt-misc tbody").TextContent;
        Assert.Contains("Settled", body);
        Assert.Contains("Part-settled", body);
        Assert.Contains("Unpaid", body);
        Assert.DoesNotContain("₱", body);

        var section = cut.Find("#rpt-misc").TextContent.Replace('\u00A0', ' ');
        Assert.Contains("Settled this period", section);
        Assert.Contains("2 still owing", section);

        // No total row either: there is nothing to total.
        Assert.Empty(cut.FindAll("#rpt-misc tfoot"));
    }

    [Fact]
    public void Miscellaneous_SaysNothingWasBilled_RatherThanShowingAnEmptyTable()
    {
        // A month with no bill, or a yearly view, which is billed and filed a month at a time. The same principle the
        // facility table follows for a facility with nothing billed: state it, do not draw noughts.
        var cut = RenderReport(SampleReport());   // the sample carries no Misc

        OpenSection(cut, "Misc");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#rpt-misc")), RenderTimeout);

        Assert.Empty(cut.FindAll("#rpt-misc table"));
        Assert.Contains("Nothing billed", cut.Find("#rpt-misc").TextContent);
    }

    [Fact]
    public void TheRateBasisNote_IsHiddenUntilHoveredOrPressed()
    {
        // Hover and keyboard focus reveal it in CSS, so the note has to be in the markup at all times — what changes on a
        // press is only whether it is pinned open. Asserting on the class is therefore the honest test; asserting the
        // element's absence would be asserting the opposite of how it works.
        var cut = RenderReport(SampleReport());

        OpenSection(cut, "By facility");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".rpt-rule-mark")), RenderTimeout);

        var mark = cut.Find(".rpt-rule-mark");
        Assert.Equal("false", mark.GetAttribute("aria-expanded"));
        Assert.DoesNotContain("is-open", cut.Find("#rpt-facility-rule").GetAttribute("class"));

        mark.Click();

        Assert.Equal("true", cut.Find(".rpt-rule-mark").GetAttribute("aria-expanded"));
        Assert.Contains("is-open", cut.Find("#rpt-facility-rule").GetAttribute("class"));

        var note = cut.Find("#rpt-facility-rule").TextContent;
        Assert.Contains("Collected ÷ (collected + unpaid)", note);
        Assert.Contains("across 8 facilities", note);          // the sample report's own count, not a literal in the page
        Assert.Contains("daily fees against daily fees due", note);

        cut.Find(".rpt-rule-mark").Click();

        Assert.DoesNotContain("is-open", cut.Find("#rpt-facility-rule").GetAttribute("class"));
    }

    [Fact]
    public void RendersOneMonthDelinquency_AndKeepsArrearsUnresolved()
    {
        var cut = RenderReport(SampleReport());

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Delinquent accounts", cut.Markup);
            Assert.Contains("At least one fully elapsed unpaid month", cut.Markup);
            Assert.Contains("Arrears qualification", cut.Markup);
            Assert.Contains("old/lapsed qualification boundary is not defined", cut.Markup);
            Assert.Contains("Rosa Magbanua", cut.Markup);
            Assert.Contains("3 unpaid months", cut.Markup);
            Assert.Contains("Jose Dalumpines", cut.Markup);
            Assert.DoesNotContain("Accounts in arrears", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public void NpmRow_Expands_To_Show_Fish_And_FullMonthCoverage()
    {
        var cut = RenderReport(SampleReport());

        // No detail strip until the NPM row is expanded.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".fac-expandable")), RenderTimeout);
        Assert.Empty(cut.FindAll(".fac-detail-row"));

        cut.FindAll(".fac-expandable").First().Click();

        Assert.Single(cut.FindAll(".fac-detail-row"));
        Assert.Contains("Full-month coverage balance", cut.Markup);
        Assert.Contains("₱1,890", cut.Markup);   // per-stall coverage balance
        Assert.Contains("Fish", cut.Markup);      // fish split line
        Assert.Contains("Electricity", cut.Markup); // electricity & water utility breakdown
        Assert.Contains("₱320", cut.Markup);        // electricity collected
    }

    [Fact]
    public void AttentionList_MarksALapsedTerm_AndStatesTheWholeOutstanding()
    {
        var cut = RenderReport(SampleReport());

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() =>
        {
            // The whole unsettled position, not a twelve-month slice: 37 months and ₱33,300, which is what the
            // Closed / Inactive register states for the same account.
            Assert.Contains("37 unpaid months", cut.Markup);
            Assert.Contains("33,300", cut.Markup);

            // And the row says the term has run out, so the office can tell a lapsed tenancy from a live one
            // without opening the register.
            var lapsed = cut.FindAll(".attn-lapsed");
            Assert.Single(lapsed);
            Assert.Equal("Lapsed", lapsed[0].TextContent.Trim());
        }, RenderTimeout);
    }

    /// <summary>
    /// A Recent Records payor links to the stall by ID, not by its number.
    /// </summary>
    /// <remarks>
    /// The market numbers spaces per section, so one facility holds several "Stall 1" and a facility-and-number link opens whichever
    /// the lookup finds first — one payor's row opening another's profile. Fixed for the attention rows and the follow-up queue in
    /// 7d4b2bc2; this row kept passing the number because FinancialRecordDto carried no id to pass.
    ///
    /// <para>The number here is deliberately "1", the one that repeats, and the href is asserted WHOLE rather than searched for the
    /// id: a Contains would still pass if the number were the thing in the link.</para>
    /// </remarks>
    [Fact]
    public void RecentRecords_LinksThePayorToTheStallById()
    {
        var stallId = Guid.NewGuid();
        var dto = SampleReport() with
        {
            RecentRecords = new List<FinancialRecordDto>
            {
                new("OR-9", "Luz Cano", FacilityCode.NPM, "1", new DateTime(2026, 3, 25), null, "Daily Fee", 930m, stallId)
            }
        };

        var cut = RenderReport(dto);

        OpenSection(cut, "Records");

        cut.WaitForAssertion(() =>
        {
            var link = cut.FindAll("a.vendor-link").Single(a => a.TextContent.Trim() == "Luz Cano");

            Assert.Equal($"/profile/npm/{stallId}", link.GetAttribute("href"));
        }, RenderTimeout);
    }

    /// <summary>A row about no stall still links as it always did — slaughter, terminal trips and market-day vendors have none.</summary>
    [Fact]
    public void RecentRecords_WithNoStall_FallsBackToTheReference()
    {
        var dto = SampleReport() with
        {
            RecentRecords = new List<FinancialRecordDto>
            {
                new("OR-9", "Ramon Dy", FacilityCode.TRM, "ABC-123", new DateTime(2026, 3, 25), null, "Terminal Trip", 30m)
            }
        };

        var cut = RenderReport(dto);

        OpenSection(cut, "Records");

        cut.WaitForAssertion(() =>
        {
            var link = cut.FindAll("a.vendor-link").Single(a => a.TextContent.Trim() == "Ramon Dy");

            Assert.Equal("/profile/trm/ABC-123", link.GetAttribute("href"));
        }, RenderTimeout);
    }

    [Fact]
    public void AttentionList_SearchFiltersEachColumnIndependently()
    {
        var cut = RenderReport(SampleReport());

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".attn-search input")), RenderTimeout);

        // Search narrows the one delinquency list; unresolved Arrears is not represented as an age-based people list.
        cut.FindAll(".attn-search input")[0].Input("merlita");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Merlita A. Abuso", cut.Markup);
            Assert.DoesNotContain("Rosa Magbanua", cut.Markup);
            Assert.DoesNotContain("Jose Dalumpines", cut.Markup);
        }, RenderTimeout);

        // A term with no match says so rather than showing an empty panel.
        cut.FindAll(".attn-search input")[0].Input("zzzz");
        cut.WaitForAssertion(() => Assert.Contains("No delinquent account matches", cut.Markup), RenderTimeout);
    }

    [Fact]
    public void TheFollowUpHeaderStatesTheWholeDebt_NotTheVisibleRows()
    {
        // The page lists at most 50 accounts per column. Its header used to count and sum those rows while calling the
        // result "outstanding in full", so an office past the cap read a smaller number of accounts and less money than it
        // was owed. The header must come from the report's totals.
        var report = SampleReport() with
        {
            DelinquentAccountsTotal = 63,
            DelinquentOutstandingTotal = 500_000m,
            ArrearsAccountsTotal = null,
            ArrearsOutstandingTotal = null,
        };

        var cut = RenderReport(report);

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() =>
        {
            // The header states figures rather than a sentence now, so they are read off the elements that hold them —
            // a raw string match would break on the CSS-isolation attribute Blazor adds to the class. What the test is
            // really holding is unchanged: the delinquent count and balance come from report TOTALS, not the rows rendered.
            var stats = cut.FindAll(".rpt-attn-stat-val").Select(e => e.TextContent.Trim()).ToList();
            Assert.Contains("63", stats);
            Assert.Contains("₱500,000", stats);
            Assert.Contains("delinquent balance", cut.Markup);
        }, RenderTimeout);
    }

    [Fact]
    public void ACappedListSaysSo()
    {
        // Honesty about the list itself: the figures are complete, the rows are not, and the page says which.
        var report = SampleReport() with
        {
            DelinquentAccountsTotal = 63,
            DelinquentOutstandingTotal = 500_000m,
        };

        var cut = RenderReport(report);

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() =>
        {
            var note = cut.Find(".attn-capped").TextContent;
            Assert.Contains("63", note);
            Assert.Contains("3", note);   // the three delinquent rows the fixture carries
        }, RenderTimeout);
    }

    [Fact]
    public void AnUncappedListSaysNothingAboutBeingCapped()
    {
        // The ordinary case must stay quiet — a note on every report would be noise, and would train the office to
        // disregard it on the one report where it matters.
        var cut = RenderReport(SampleReport());

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() => Assert.Contains("Rosa Magbanua", cut.Markup), RenderTimeout);
        Assert.Empty(cut.FindAll(".attn-capped"));
    }

    [Fact]
    public void TheColumnCountsStateEveryAccount()
    {
        var report = SampleReport() with { DelinquentAccountsTotal = 63, ArrearsAccountsTotal = null };

        var cut = RenderReport(report);

        OpenSection(cut, "Follow-up");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("63", cut.Find(".attn-count-red").TextContent.Trim());
            Assert.Empty(cut.FindAll(".attn-count-amber"));
            Assert.Contains("Arrears qualification", cut.Markup);
        }, RenderTimeout);
    }
}
