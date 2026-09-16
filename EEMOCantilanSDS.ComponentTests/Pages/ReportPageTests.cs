using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
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
            new("Merlita A. Abuso", FacilityCode.ICE, "7", "ICE · Stall 7", 33_300m, 37, TermLapsed: true)
        },
        Arrears: new List<AttentionAccountDto>
        {
            new("Jose Dalumpines", FacilityCode.NCC, "11-B", "NCC · Stall 11-B", 3_600m, 2)
        },
        // The three accounts above, aged: Jose at 2 months, Rosa at 3, Merlita at 37. The bands reconcile with the
        // delinquent and arrears totals rather than offering a second count of the same debt.
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
        // Set to agree with the two lists above: below the display cap, the totals and the lists describe the same
        // accounts. Left at their defaults these would be nought, and the page header states them.
        DelinquentAccountsTotal: 2,
        DelinquentOutstandingTotal: 38_100m,   // 4,800 + 33,300
        ArrearsAccountsTotal: 1,
        ArrearsOutstandingTotal: 3_600m);

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
    public void Renders_DelinquentAndArrears_Separately()
    {
        var cut = RenderReport(SampleReport());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Delinquent accounts", cut.Markup);
            Assert.Contains("Accounts in arrears", cut.Markup);
            Assert.Contains("Rosa Magbanua", cut.Markup);
            Assert.Contains("3 unpaid months", cut.Markup);
            Assert.Contains("Jose Dalumpines", cut.Markup);
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

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".attn-search input").Count), RenderTimeout);

        // Typing in the delinquent column's box narrows that column and leaves the arrears column alone.
        cut.FindAll(".attn-search input")[0].Input("merlita");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Merlita A. Abuso", cut.Markup);
            Assert.DoesNotContain("Rosa Magbanua", cut.Markup);
            Assert.Contains("Jose Dalumpines", cut.Markup);
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
            ArrearsAccountsTotal = 12,
            ArrearsOutstandingTotal = 40_000m,
        };

        var cut = RenderReport(report);

        cut.WaitForAssertion(() =>
        {
            // The header states figures rather than a sentence now, so they are read off the elements that hold them —
            // a raw string match would break on the CSS-isolation attribute Blazor adds to the class. What the test is
            // really holding is unchanged: both come from the report's TOTALS (63 + 12 accounts, ₱500,000 + ₱40,000) and
            // not from the three rows the fixture actually renders.
            var stats = cut.FindAll(".rpt-attn-stat-val").Select(e => e.TextContent.Trim()).ToList();
            Assert.Contains("75", stats);
            Assert.Contains("₱540,000", stats);
            Assert.Contains("outstanding in full", cut.Markup);
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

        cut.WaitForAssertion(() =>
        {
            var note = cut.Find(".attn-capped").TextContent;
            Assert.Contains("63", note);
            Assert.Contains("2", note);   // the two rows the fixture carries
        }, RenderTimeout);
    }

    [Fact]
    public void AnUncappedListSaysNothingAboutBeingCapped()
    {
        // The ordinary case must stay quiet — a note on every report would be noise, and would train the office to
        // disregard it on the one report where it matters.
        var cut = RenderReport(SampleReport());

        cut.WaitForAssertion(() => Assert.Contains("Rosa Magbanua", cut.Markup), RenderTimeout);
        Assert.Empty(cut.FindAll(".attn-capped"));
    }

    [Fact]
    public void TheColumnCountsStateEveryAccount()
    {
        var report = SampleReport() with { DelinquentAccountsTotal = 63, ArrearsAccountsTotal = 12 };

        var cut = RenderReport(report);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("63", cut.Find(".attn-count-red").TextContent.Trim());
            Assert.Equal("12", cut.Find(".attn-count-amber").TextContent.Trim());
        }, RenderTimeout);
    }
}
