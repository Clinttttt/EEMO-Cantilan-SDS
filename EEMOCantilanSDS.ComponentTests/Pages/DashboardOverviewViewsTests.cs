using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Dashboard's one managerial region: Operations (facility cards plus the direct-collection and space operations that the
/// official source projection supplies) or Revenue sources (the single official table), the Income From Terminal card, and
/// hero figures that state what they cover.
/// </summary>
public sealed class DashboardOverviewViewsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficialReportsApiClient> _official = new();
    private readonly Mock<IOfficeSourcesApiClient> _office = new();
    private readonly Mock<IDashboardApiClient> _dashboard = new();

    public DashboardOverviewViewsTests()
    {
        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(api => api.GetCurrentBrandingAsync()).ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalities.Object));
        Services.AddSingleton(_dashboard.Object);
        Services.AddSingleton(_official.Object);
        Services.AddSingleton(_office.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        var facilities = Mock.Of<IFacilitiesApiClient>();
        Services.AddSingleton(facilities);
        Services.AddSingleton(new FacilityState(facilities));
        this.AddTestAuthorization().SetNotAuthorized();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        _office.Setup(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), CollectorOperationCodes.Terminal))
            .ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]));
    }

    private static DashboardFacilityDto Facility(FacilityCode code, string name, string shortName, decimal collected = 5000m) =>
        new(code, name, shortName, collected, 1, 4, 0, 6, 80);

    private void Dashboard(decimal pending = 1200m, int unpaid = 3, int paid = 9, int rate = 80)
    {
        _dashboard.Setup(api => api.GetOverviewAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<DashboardOverviewDto>.Success(
            new DashboardOverviewDto(5000m + 700m, pending, unpaid, paid, rate, 2, 4,
                [Facility(FacilityCode.NPM, "New Public Market", "NPM"), Facility(FacilityCode.TRM, "Transport Terminal", "TRM", 700m)],
                [], [])));
    }

    private static RevenueSourcePerformanceRowDto Row(string key, string label, string group, string groupLabel, string model, decimal collected,
        int? transactions = 1, FacilityCode? facility = null, bool awaiting = false) =>
        new(key, label, group, groupLabel, model, "OR", facility, collected, 0m, collected, transactions, transactions, 1, "Active", awaiting);

    private RevenueSourcePerformanceDto Source(params RevenueSourcePerformanceRowDto[] rows) =>
        new(PhilippineTime.Today.Year, PhilippineTime.Today.Month,
            [new("MARKET", "Income From Market", 0m)], rows, 98765m, [], DateTime.UtcNow);

    private static RevenueSourcePerformanceRowDto[] FullCatalog() =>
    [
        Row("MARKET_FEES", "Market Fees", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 1000m, 40),
        Row("ECF", "Electricity Consumption Fees", "MARKET", "Income From Market", RevenueSourceModel.RecurringObligation, 2000m, 12),
        Row("WCF", "Water Consumption Fees", "MARKET", "Income From Market", RevenueSourceModel.RecurringObligation, 300m, null),
        Row("FISH_MEAT_VENDOR_FEE", "Fish | Meat Vendor Fees", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 400m, 8),
        Row("LANDING_BERTHING", "Landing / Berthing", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 500m, 5),
        Row("TRANSPORTATION_PARKING", "Transportation | Parking", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 600m, 30),
        Row("WEIGHT_AND_MEASURE", "Weight & Measure | Registration", "MARKET", "Income From Market", RevenueSourceModel.QuantityService, 700m, 9),
        Row("TRANSFER_LARGE_CATTLE", "Transfer Large Cattle", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 0m, 0),
        Row("VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable | Fruits", "SPACE", "Space Rental", RevenueSourceModel.Transactional, 800m, 22),
        Row("KANMANGGAY_SPACE_RENTAL", "Kanmanggay", "SPACE", "Space Rental", RevenueSourceModel.RecurringObligation, 900m, 11),
        Row("FIESTA_ARAW_LOT_RENTAL", "Fiesta | Araw", "SPACE", "Space Rental", RevenueSourceModel.EventRental, 0m, 0),
        Row("PENALTIES_AND_FINES", "Penalties and Fines", "OTHER", "Other operations", RevenueSourceModel.Transactional, 250m, 4),
        // Facility-backed, terminal, slaughterhouse and tabo rows already have their own cards; a receivable and an unknown classification have no workspace.
        Row("RENT_NPM", "Rent — NPM", "RENT", "Rent Income", RevenueSourceModel.RecurringObligation, 5000m, 100, FacilityCode.NPM),
        Row("ICE_PLANT", "Ice Plant", "MARKET", "Income From Market", RevenueSourceModel.RecurringObligation, 100m, 3, FacilityCode.ICE),
        Row("TABO", "Tabo", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 120m, 6),
        Row("SLAUGHTERHOUSE", "Slaughterhouse", "SLAUGHTERHOUSE", "Income From Slaughterhouse", RevenueSourceModel.QuantityService, 330m, 7),
        Row("TERMINAL_COMFORT_ROOM", "Terminal — Comfort Room", "TERMINAL", "Income From Terminal", RevenueSourceModel.Transactional, 700m, 20),
        Row("ARREARS", "Arrears", "RECEIVABLE", "Receivables context", RevenueSourceModel.Receivable, 0m, null),
        Row("OTHER_UNMAPPED", "Unmapped classification", "OTHER", "Other operations", RevenueSourceModel.Transactional, 50m, 1),
    ];

    private void ServeSource(params RevenueSourcePerformanceRowDto[] rows) =>
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(Source(rows)));

    private IRenderedComponent<Menu> RenderLoaded()
    {
        var cut = RenderComponent<Menu>();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[aria-label='Loading other operations']")), Timeout);
        return cut;
    }

    private static void Switch(IRenderedComponent<Menu> cut, string label) =>
        cut.FindAll(".dashboard-view-switch button").Single(b => b.TextContent.Trim() == label).Click();

    [Fact]
    public void TheSwitchDefaultsToOperations_AndTheRevenueTableIsNotMountedUntilChosen()
    {
        Dashboard(); ServeSource(FullCatalog());

        var cut = RenderLoaded();

        var buttons = cut.FindAll(".dashboard-view-switch button");
        Assert.Equal(["Operations", "Revenue sources"], buttons.Select(b => b.TextContent.Trim()).ToArray());
        Assert.Equal("true", buttons[0].GetAttribute("aria-pressed")); Assert.Equal("false", buttons[1].GetAttribute("aria-pressed"));
        Assert.Contains("Operations overview", cut.Find("#dash-view-heading").TextContent);
        Assert.Empty(cut.FindComponents<RevenueSourcePerformancePanel>());
        Assert.Empty(cut.FindAll("#dash-view-revenue"));
    }

    [Fact]
    public void ChoosingRevenueSourcesShowsExactlyOneOfficialTable_AndSwitchingBackKeepsOperationsWithoutRereading()
    {
        Dashboard(); ServeSource(FullCatalog());
        var cut = RenderLoaded();

        Switch(cut, "Revenue sources");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("table.rsp-table")), Timeout);
        Assert.Single(cut.FindComponents<RevenueSourcePerformancePanel>());
        Assert.Contains("Revenue source performance", cut.Find("#dash-view-heading").TextContent);
        Assert.Contains("Revenue source performance", cut.Find("#rsp-title").TextContent);   // the table's own heading stays for screen readers
        Assert.Null(cut.Find("#dash-view-revenue").GetAttribute("hidden")); Assert.NotNull(cut.Find("#dash-view-operations").GetAttribute("hidden"));
        // Annual target stays read-only on Overview; the editor belongs to Reports.
        Assert.Empty(cut.FindAll(".rsp-target-btn"));

        Switch(cut, "Operations");
        Assert.Contains("Operations overview", cut.Find("#dash-view-heading").TextContent);
        Assert.Null(cut.Find("#dash-view-operations").GetAttribute("hidden")); Assert.NotNull(cut.Find("#dash-view-revenue").GetAttribute("hidden"));
        Assert.Equal("true", cut.FindAll(".dashboard-view-switch button")[0].GetAttribute("aria-pressed"));
        Assert.Single(cut.FindComponents<RevenueSourcePerformancePanel>());   // hidden, never remounted

        for (var i = 0; i < 3; i++) { Switch(cut, "Revenue sources"); Switch(cut, "Operations"); }

        _official.Verify(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Once);
        _office.Verify(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), CollectorOperationCodes.Terminal), Times.Once);
    }

    [Fact]
    public void TheRevenueSourceFilterKeepsItsStateAcrossSwitches()
    {
        Dashboard(); ServeSource(FullCatalog());
        var cut = RenderLoaded();
        Switch(cut, "Revenue sources");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("table.rsp-table")), Timeout);
        var panel = cut.FindComponent<RevenueSourcePerformancePanel>();

        Switch(cut, "Operations"); Switch(cut, "Revenue sources");

        Assert.Same(panel.Instance, cut.FindComponent<RevenueSourcePerformancePanel>().Instance);
    }

    [Fact]
    public void OperationsCoverTheDirectAndSpaceSourcesOnce_WithTruthfulMetrics_AndRouteToTheirWorkspaces()
    {
        Dashboard(); ServeSource(FullCatalog());

        var cut = RenderLoaded();

        var cards = cut.FindAll(".facility-card[data-source-key]");
        Assert.Equal(DashboardOperations.Direct.Select(e => e.Key).ToArray(), cards.Select(c => c.GetAttribute("data-source-key")).ToArray());
        foreach (var entry in DashboardOperations.Direct)
            Assert.Equal(entry.Route, cards.Single(c => c.GetAttribute("data-source-key") == entry.Key).QuerySelector("a.facility-card-btn")!.GetAttribute("href"));

        // Facility-backed, Terminal, Slaughterhouse and Tabo rows have their own cards; arrears and unmapped rows have no workspace.
        foreach (var absent in new[] { "RENT_NPM", "ICE_PLANT", "TABO", "SLAUGHTERHOUSE", "TERMINAL_COMFORT_ROOM", "ARREARS", "OTHER_UNMAPPED" })
            Assert.DoesNotContain(cards, c => c.GetAttribute("data-source-key") == absent);

        var market = cards.Single(c => c.GetAttribute("data-source-key") == "MARKET_FEES");
        Assert.Equal(["₱1,000", "40"], market.QuerySelectorAll(".fac-stat-val").Select(v => v.TextContent.Trim()).ToArray());
        Assert.Contains("Income From Market", market.TextContent); Assert.Contains("Paid on service", market.TextContent);
        // A direct service has no obligation denominator: no collection rate, progress bar or stall counts, and an unavailable count is a dash.
        Assert.Empty(market.QuerySelectorAll(".facility-progress-wrap")); Assert.DoesNotContain("%", market.TextContent);
        Assert.Equal("—", cards.Single(c => c.GetAttribute("data-source-key") == "WCF").QuerySelectorAll(".fac-stat-val")[1].TextContent.Trim());
        // A real zero is shown as a zero, because the server answered.
        Assert.Equal(["₱0", "0"], cards.Single(c => c.GetAttribute("data-source-key") == "TRANSFER_LARGE_CATTLE").QuerySelectorAll(".fac-stat-val").Select(v => v.TextContent.Trim()).ToArray());
        // Each source appears under exactly one card: facilities plus direct cards, never the same operation twice.
        Assert.Equal(cards.Count, cards.Select(c => c.GetAttribute("data-source-key")).Distinct().Count());
    }

    [Fact]
    public void ARowAwaitingOfficialPlacementSaysSoOnItsCard()
    {
        Dashboard(); ServeSource(Row("PENALTIES_AND_FINES", "Penalties and Fines", "OTHER", "Other operations", RevenueSourceModel.Transactional, 250m, 4, awaiting: true));

        var cut = RenderLoaded();

        Assert.Contains("Needs official placement", cut.Find(".facility-card[data-source-key='PENALTIES_AND_FINES']").TextContent);
    }

    [Fact]
    public void TheDashboardRoutesMatchTheOperationsRegister()
    {
        var operations = File.ReadAllText("../../../../EEMOCantilanSDS.Client/Components/Pages/Menus/Operations.razor");
        foreach (var entry in DashboardOperations.Direct.Where(e => e.Key != "PENALTIES_AND_FINES"))
            Assert.Contains($"\"{entry.Route}\"", operations);
        Assert.Contains("\"/operations/fines\"", operations);
    }

    [Fact]
    public void IncomeFromTerminalIsTheSameCardFamily_WithItsOwnTwoFigures()
    {
        Dashboard(); ServeSource(FullCatalog());

        var cut = RenderLoaded();

        var terminal = cut.Find(".terminal-card");
        Assert.Equal("IFT", terminal.QuerySelector("small")!.TextContent);
        Assert.Equal("/operations/terminal", terminal.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(2, terminal.QuerySelectorAll("dd").Length);
        Assert.DoesNotContain("TRM", terminal.TextContent);
        // It rides in the facility grid beside the other pale cards, not as a separate white block.
        Assert.NotNull(terminal.ParentElement!.ClassList.Contains("facility-grid") ? terminal : null);

        var css = File.ReadAllText("../../../../EEMOCantilanSDS.Client/Components/Shared/TerminalOverviewCard.razor.css");
        Assert.Contains("background: var(--surface-blue-subtle)", css);
        Assert.Contains("border: 1px solid var(--border-blue-subtle)", css);
        Assert.DoesNotContain("var(--white", css);
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }

    [Fact]
    public void TheHeroStatesItsScope_AndCollectedIsTheOfficialAllSourceTotal()
    {
        Dashboard(pending: 1200m, unpaid: 3, paid: 9, rate: 80); ServeSource(FullCatalog());

        var cut = RenderLoaded();

        var figures = cut.FindAll(".v3h-figure").ToDictionary(f => f.QuerySelector(".v3h-key")!.TextContent.Trim(), f => f);
        Assert.Equal(["Collected", "Outstanding rent", "Facility payments", "Facility collection rate"], figures.Keys.ToArray());
        // The official total, not the facility sum (₱5,700).
        Assert.Equal("₱98,765", figures["Collected"].QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("all revenue sources", figures["Collected"].TextContent);
        Assert.Equal("₱1,200", figures["Outstanding rent"].QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("facility rentals only", figures["Outstanding rent"].TextContent);
        Assert.Contains("not every source", figures["Facility payments"].TextContent);
        Assert.Equal("80%", figures["Facility collection rate"].QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("stall rentals", figures["Facility collection rate"].TextContent);
    }

    [Fact]
    public void AFailedSourceReadIsUnavailableEverywhere_NeverAZero_AndRetryRecovers()
    {
        Dashboard();
        var calls = 0;
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(() =>
            ++calls == 1 ? Result<RevenueSourcePerformanceDto>.Failure("offline") : Result<RevenueSourcePerformanceDto>.Success(Source(FullCatalog())));

        var cut = RenderComponent<Menu>();

        cut.WaitForAssertion(() => Assert.Contains("could not be loaded", cut.Find(".dash-note-error").TextContent), Timeout);
        Assert.Empty(cut.FindAll(".facility-card[data-source-key]"));
        var collected = cut.FindAll(".v3h-figure").Single(f => f.TextContent.Contains("Collected") && !f.TextContent.Contains("rate"));
        Assert.Equal("—", collected.QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("Official total unavailable", collected.TextContent);
        Assert.Contains("₱5,000", cut.Markup);   // the facility cards, whose own source loaded, are unaffected

        cut.Find(".dash-note-error button").Click();

        cut.WaitForAssertion(() => Assert.Equal(12, cut.FindAll(".facility-card[data-source-key]").Count), Timeout);
        Assert.Equal("₱98,765", cut.FindAll(".v3h-figure").First().QuerySelector(".v3h-val")!.TextContent.Trim());
    }

    [Fact]
    public void PrerenderingDoesNotReadTheOfficialProjection()
    {
        Dashboard(); ServeSource(FullCatalog());
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var cut = RenderComponent<Menu>();
        cut.WaitForAssertion(() => Assert.Contains("Operations overview", cut.Markup), Timeout);

        _official.Verify(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
        Assert.NotEmpty(cut.FindAll("[aria-label='Loading other operations']"));
    }

    [Fact]
    public void ThePanelUsesTheHandedInProjectionWithoutASecondRead_AndAMismatchedPeriodStillLoadsItself()
    {
        var shared = Source(Row("LANDING_BERTHING", "Landing / Berthing", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 500m, 5));
        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, shared.Year).Add(x => x.Month, shared.Month)
            .Add(x => x.OverviewMode, true).Add(x => x.Embedded, true).Add(x => x.Preloaded, shared));

        Assert.Contains("Landing / Berthing", cut.Markup);
        _official.Verify(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
        // Embedded keeps the heading for screen readers while the host's section header is the visible title.
        Assert.Contains("v3-sr-only", cut.Find(".rsp-head-copy").ClassName);
        Assert.Contains("Revenue source performance", cut.Find("#rsp-title").TextContent);

        ServeSource(Row("MARKET_FEES", "Market Fees", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 10m, 1));
        var other = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, shared.Year).Add(x => x.Month, shared.Month == 1 ? 12 : shared.Month - 1)
            .Add(x => x.OverviewMode, true).Add(x => x.Preloaded, shared));
        other.WaitForAssertion(() => Assert.Contains("Market Fees", other.Markup), Timeout);
        _official.Verify(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public void RendersBothViewsForVisualReview()
    {
        Dashboard(); ServeSource(FullCatalog());
        var cut = RenderLoaded();
        Assert.NotEmpty(cut.FindAll(".facility-card"));
        Export("dashboard-operations", cut.Markup);
        Switch(cut, "Revenue sources");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("table.rsp-table")), Timeout);
        Export("dashboard-revenue-sources", cut.Markup);
    }

    private static void Export(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("STALLTRACK_FINE_FIXTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var links = new[] { "EEMOCantilanSDS.Client/wwwroot/css/site.min.css", "EEMOCantilanSDS.Client/wwwroot/app.css",
            "EEMOCantilanSDS.Client/obj/Release/net10.0/scopedcss/bundle/EEMOCantilanSDS.Client.styles.css" }
            .Select(path => $"<link rel='stylesheet' href='{new Uri(Path.Combine(root, path)).AbsoluteUri}'>");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".html"), "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"
            + string.Join("", links) + "</head><body><main>" + markup + "</main></body></html>", new System.Text.UTF8Encoding(false));
    }

    [Fact]
    public void TheViewRegionHasASubtleBoundedTransition_ThatRespectsReducedMotion()
    {
        var css = File.ReadAllText("../../../../EEMOCantilanSDS.Client/Components/Pages/Menus/Menu.razor.css");
        Assert.Matches(@"\.dash-view\s*\{\s*animation:\s*dash-view-in\s+(1[5-9]\d|2[01]\d|220)ms", css);
        Assert.Matches(@"prefers-reduced-motion:\s*reduce\)\s*\{[^@]*\.dash-view\s*\{\s*animation:\s*none", css);
        Assert.Contains(".dash-view[hidden] { display: none; }", css);
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }
}
