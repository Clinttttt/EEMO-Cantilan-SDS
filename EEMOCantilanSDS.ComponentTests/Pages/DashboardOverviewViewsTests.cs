using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Reports;
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
    private readonly Mock<IReportsApiClient> _financialReports = new();
    private readonly Mock<IOperationSettingsApiClient> _tenantSettingsApi = new();
    private readonly OperationNameService _tenantNames;

    public DashboardOverviewViewsTests()
    {
        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(api => api.GetCurrentBrandingAsync()).ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalities.Object));
        Services.AddSingleton(_dashboard.Object);
        Services.AddSingleton(_official.Object);
        Services.AddSingleton(_financialReports.Object);
        Services.AddSingleton(_office.Object);
        _tenantNames = new OperationNameService(_tenantSettingsApi.Object);
        Services.AddSingleton(_tenantNames);
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

    [Fact]
    public void Dashboard_collected_keeps_1200_cash_and_official_revision_is_labeled_separately()
    {
        Dashboard();
        var row = Row("RENT_NCC", "NCC", "RENT", "Rent income", RevenueSourceModel.RecurringObligation, 1200, facility: FacilityCode.NCC)
            with { AdjustmentAmount = 54, AnnualTarget = 2000, Attainment = 62.7m };
        var data = Source(row) with { TotalCollected = 1200, Groups = [new("RENT", "Rent income", 1200)] };
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(data));
        var cut = RenderComponent<Menu>();
        cut.WaitForAssertion(() => Assert.Contains("1,200", cut.FindAll(".v3h-figure").Single(x => x.TextContent.Trim().StartsWith("Collected")).TextContent));
        Assert.DoesNotContain("1,254", cut.FindAll(".v3h-figure").Single(x => x.TextContent.Trim().StartsWith("Collected")).TextContent);
        var panel = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, data.Year).Add(x => x.Month, data.Month));
        panel.WaitForAssertion(() => Assert.Contains("Official", panel.Find(".rsp-collected").TextContent));
        Assert.Contains("1,254", panel.Find(".rsp-collected").TextContent); Assert.Contains("report adjustment", panel.Markup);
        Assert.Contains("62.7% official", panel.Markup);
    }

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
        cut.FindAll(".dash-view:not([hidden]) .dashboard-view-switch button").Single(b => b.TextContent.Trim() == label).Click();

    [Fact]
    public async Task AdoptedOffice_OverviewShowsOnlySelectedOperationsAndHistoricalActivity()
    {
        var settings = OperationsTenantConfigurationTests.SettingsApi(adopted: true,
            listed: ["MARKET_FEES", "TRANSPORTATION"]);
        _tenantSettingsApi.Setup(c => c.GetAsync()).Returns(() => settings.Object.GetAsync());
        await _tenantNames.EnsureLoadedAsync();
        Dashboard();
        ServeSource(
            Row("MARKET_FEES", "Market Fees", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 0m, 0),
            Row("TRANSPORTATION_PARKING", "Transportation", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 0m, 0),
            Row("FISH_MEAT_VENDOR_FEE", "Fish / Meat", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 0m, 0),
            Row("WEIGHT_AND_MEASURE", "Weight & Measure", "MARKET", "Income From Market", RevenueSourceModel.QuantityService, 25m, 1),
            Row("PENALTIES_AND_FINES", "Fines", "OTHER", "Other operations", RevenueSourceModel.Transactional, 0m, 0));

        var cut = RenderLoaded();
        cut.WaitForAssertion(() =>
        {
            var keys = cut.FindAll(".facility-card[data-source-key]")
                .Select(card => card.GetAttribute("data-source-key")).ToArray();
            Assert.Equal(["MARKET_FEES", "TRANSPORTATION_PARKING", "WEIGHT_AND_MEASURE"], keys);
            Assert.DoesNotContain("PENALTIES_AND_FINES", keys);
        }, Timeout);
    }

    [Fact]
    public async Task AdoptedOffice_RevenueTableHidesUnselectedZeroRowsButKeepsCashAndTotal()
    {
        var settings = OperationsTenantConfigurationTests.SettingsApi(adopted: true,
            listed: ["MARKET_FEES"],
            labels: new Dictionary<string, string> { ["MARKET_FEES"] = "Carmen Market Fee" });
        _tenantSettingsApi.Setup(c => c.GetAsync()).Returns(() => settings.Object.GetAsync());
        await _tenantNames.EnsureLoadedAsync();
        var source = Source(
            Row("MARKET_FEES", "Market Fees", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 0m, 0),
            Row("LANDING_BERTHING", "Landing", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 0m, 0),
            Row("RENT_NCC", "NCC Rent", "RENT", "Rent Income", RevenueSourceModel.RecurringObligation, 0m, 0, FacilityCode.NCC),
            Row("RENT_NPM", "NPM Rent", "RENT", "Rent Income", RevenueSourceModel.RecurringObligation, 0m, 0, FacilityCode.NPM),
            Row("PENALTIES_AND_FINES", "Fines", "OTHER", "Other operations", RevenueSourceModel.Transactional, 0m, 0),
            Row("OTHER_HISTORICAL", "Legacy source", "OTHER", "Other operations", RevenueSourceModel.Transactional, 120m, null));
        source = source with
        {
            TotalCollected = 120m,
            Groups = [new("MARKET", "Income From Market", 0m), new("RENT", "Rent Income", 0m),
                new("OTHER", "Other operations", 120m)]
        };

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p
            .Add(x => x.Year, source.Year).Add(x => x.Month, source.Month)
            .Add(x => x.Preloaded, source)
            .Add(x => x.ConfiguredFacilityCodes, [FacilityCode.NPM]));
        cut.WaitForAssertion(() =>
        {
            var labels = cut.FindAll("tr.rsp-row th.rsp-source").Select(x => x.TextContent.Trim()).ToArray();
            Assert.Equal(["Carmen Market Fee", "NPM Rent", "Legacy source"], labels);
            Assert.DoesNotContain("Fines", labels);
            Assert.Contains("120", cut.Find("tfoot tr").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheSwitchDefaultsToOperations_AndTheRevenueTableIsNotMountedUntilChosen()
    {
        Dashboard(); ServeSource(FullCatalog());

        var cut = RenderLoaded();

        var buttons = cut.FindAll(".dash-view:not([hidden]) .dashboard-view-switch button");
        Assert.Equal(["Operations", "Revenue sources"], buttons.Select(b => b.TextContent.Trim()).ToArray());
        Assert.Equal("true", buttons[0].GetAttribute("aria-pressed")); Assert.Equal("false", buttons[1].GetAttribute("aria-pressed"));
        Assert.Equal("Facility operations", cut.Find("#dash-ops-heading").TextContent.Trim()); Assert.DoesNotContain("Operations overview", cut.Markup);
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
        Assert.DoesNotContain("v3-sr-only", cut.Find(".rsp-head-copy").ClassName);   // the title is visible, in the panel header
        Assert.Contains("Revenue source performance", cut.Find("#rsp-title").TextContent);   // the table's own heading stays for screen readers
        Assert.Null(cut.Find("#dash-view-revenue").GetAttribute("hidden")); Assert.NotNull(cut.Find("#dash-view-operations").GetAttribute("hidden"));
        // Annual target stays read-only on Overview; the editor belongs to Reports.
        Assert.Empty(cut.FindAll(".rsp-target-btn"));

        Switch(cut, "Operations");
        Assert.Equal("Facility operations", cut.Find("#dash-ops-heading").TextContent.Trim()); Assert.DoesNotContain("Operations overview", cut.Markup);
        Assert.Null(cut.Find("#dash-view-operations").GetAttribute("hidden")); Assert.NotNull(cut.Find("#dash-view-revenue").GetAttribute("hidden"));
        Assert.Equal("true", cut.FindAll(".dash-view:not([hidden]) .dashboard-view-switch button")[0].GetAttribute("aria-pressed"));
        Assert.Single(cut.FindComponents<RevenueSourcePerformancePanel>());   // hidden, never remounted

        for (var i = 0; i < 3; i++) { Switch(cut, "Revenue sources"); Switch(cut, "Operations"); }

        _official.Verify(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Once);
        _office.Verify(x => x.ActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), CollectorOperationCodes.Terminal), Times.Once);
    }

    [Fact]
    public void DashboardRevenueSources_UsesFinancialRegisterBalance_WithoutHidingLegacyRent()
    {
        Dashboard();
        var row = Row("RENT_TCC", "Commercial Center", "RENT", "Rent Income",
            RevenueSourceModel.RecurringObligation, 300m, transactions: null, facility: FacilityCode.TCC)
            with { LegacyCollected = 300m, CanonicalCollected = 0m };
        var source = Source(row) with { Groups = [new("RENT", "Rent income", 300m)], TotalCollected = 300m };
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(source));

        var today = PhilippineTime.Today;
        var facility = new FinancialFacilityRowDto(FacilityCode.TCC, "Commercial Center", "Monthly rental",
            false, 300m, 7800m, 18, 24, 75, "Partial");
        var financial = new FinancialReportDto(
            "October 2026", "All", "Monthly", 1, 300m, 7800m, 8100m, 4, 18, 24, null, null,
            [], null, [], 0, 0m, [], 300m, [facility], []);
        _financialReports.Setup(x => x.GetFinancialReportAsync(ReportPeriod.Monthly, today.Year, today.Month, null, false))
            .ReturnsAsync(Result<FinancialReportDto>.Success(financial));

        var cut = RenderLoaded();
        Switch(cut, "Revenue sources");

        cut.WaitForAssertion(() =>
        {
            var attention = cut.Find("tr.rsp-row .rsp-attention").TextContent;
            Assert.Contains("7,800 outstanding", attention);
            Assert.DoesNotContain("Legacy data included", attention);
        }, Timeout);
        _financialReports.Verify(x => x.GetFinancialReportAsync(ReportPeriod.Monthly, today.Year, today.Month, null, false), Times.Once);
        Switch(cut, "Operations");
        Switch(cut, "Revenue sources");
        _financialReports.Verify(x => x.GetFinancialReportAsync(ReportPeriod.Monthly, today.Year, today.Month, null, false), Times.Once);
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
        Assert.Equal("Collected", terminal.QuerySelector("dt")!.TextContent.Trim());
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
        Assert.Equal(["Collected", "Outstanding rent", "Facility payments", "Collection rate"], figures.Keys.ToArray());
        // The official total, not the facility sum (₱5,700).
        Assert.Equal("₱98,765", figures["Collected"].QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("all revenue sources", figures["Collected"].TextContent);
        Assert.Equal("₱1,200", figures["Outstanding rent"].QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("rentals only", figures["Outstanding rent"].TextContent);
        Assert.Contains("not all sources", figures["Facility payments"].TextContent);
        Assert.Equal("80%", figures["Collection rate"].QuerySelector(".v3h-val")!.TextContent.Trim());
        Assert.Contains("stall rentals", figures["Collection rate"].TextContent);
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
        Assert.Contains("Actual collections unavailable", collected.TextContent);
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
        cut.WaitForAssertion(() => Assert.Contains("Facility operations", cut.Markup), Timeout);

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

    [Fact]
    public void OperationsShowsOneCompactHeader_WithAllFiveShortcutsAndTheSwitch_AndNoStandaloneTitleRow()
    {
        Dashboard(); ServeSource(FullCatalog());

        var cut = RenderLoaded();

        var head = cut.Find("#dash-view-operations .dash-ops-head");
        Assert.Equal("Facility operations", head.QuerySelector("h3")!.TextContent.Trim());
        Assert.Equal(["/reports/month-end", "/reports/collections", "/reports/follow-up", "/stall-holders", "/reports/closed-accounts"],
            head.QuerySelectorAll("a.dashboard-report-tab").Select(a => a.GetAttribute("href")!).ToArray());
        Assert.Equal(2, head.QuerySelectorAll(".dashboard-view-switch button").Length);
        Assert.Empty(cut.FindAll(".section-header"));
        Assert.DoesNotContain("October 2026", cut.Find("#dash-view-operations").TextContent.Replace(" ", " "));
    }

    [Fact]
    public void RevenueSourcesPutsTheTitleShortcutsSwitchAndSourceFilterInTheOnePanelHeader()
    {
        Dashboard(); ServeSource(FullCatalog());
        var cut = RenderLoaded();

        Switch(cut, "Revenue sources");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("table.rsp-table")), Timeout);

        var head = cut.Find("#dash-view-revenue .rsp-head");
        Assert.Contains("Revenue source performance —", head.QuerySelector(".rsp-title")!.TextContent);
        var links = head.QuerySelectorAll(".rsp-head-actions a").ToDictionary(a => a.TextContent.Trim(), a => a.GetAttribute("href"));
        Assert.Equal("/reports/month-end", links["Monthly Collection Report"]);
        Assert.Equal("/reports/collections", links["Collection Manager"]);
        // The remaining reports stay reachable under one disclosure; none is dropped.
        Assert.Equal("/reports/follow-up", links["Follow-up Queue"]);
        Assert.Equal("/stall-holders", links["Holder Register"]);
        Assert.Equal("/reports/closed-accounts", links["Closed Accounts"]);
        Assert.NotNull(head.QuerySelector(".rsp-head-actions details.dashboard-more summary"));
        Assert.Equal(2, head.QuerySelectorAll(".dashboard-view-switch button").Length);
        Assert.NotNull(head.QuerySelector(".rsp-filter"));
        // Exactly one switch is visible: the Operations copy is hidden with its view.
        Assert.Single(cut.FindAll(".dash-view:not([hidden]) .dashboard-view-switch"));
    }

    [Fact]
    public void TheSharedPanelKeepsItsOwnHeadingAndHasNoHostActionsOutsideTheDashboard()
    {
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(Source(FullCatalog())));

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, Source(FullCatalog()).Year).Add(x => x.Month, Source(FullCatalog()).Month));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("table.rsp-table")), Timeout);
        Assert.Empty(cut.FindAll(".rsp-head-actions"));
        Assert.Contains("Actual collections and operating activity", cut.Find(".rsp-sub").TextContent);
        Assert.DoesNotContain("v3-sr-only", cut.Find(".rsp-head-copy").ClassName);
    }
}
