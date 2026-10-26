using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Reflection;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Stall &amp; Space Holder Register: stall rentals and the recurring space rentals in one register, each from its own
/// source, with no rate added across sources and nothing invented for a source that does not state it.
/// </summary>
public sealed class StallSpaceHolderRegisterTests : TestContext
{
    private readonly Mock<IObligationsApiClient> _obligations = new();
    private bool _failKanmanggay;

    private static ObligationAccountDto Space(ObligationKind kind, string number, string occupant, OccupancyArrangement arrangement,
        decimal? amount, DateOnly? to = null, string? reference = null, string? contractName = null) =>
        new(Guid.NewGuid(), kind, kind.ToString(), Guid.NewGuid(), occupant, null, null, number, null, null,
            new DateOnly(2025, 1, 1), to, amount, null, 0m, 0m, 0m, arrangement, reference, occupant, contractName,
            arrangement == OccupancyArrangement.SignedContract ? new DateOnly(2025, 2, 3) : null, 3, 6.5m, null);

    private IRenderedComponent<StallHolderList> Render(params ObligationAccountDto[] accounts)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallHoldersListAsync(FacilityCode.NCC, It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<StallHoldersListDto>.Success(new StallHoldersListDto
            {
                Sections =
                [
                    new StallHoldersSectionDto
                    {
                        SectionName = "All Stalls",
                        Rows =
                        [
                            new StallHolderRowDto
                            {
                                StallNo = "12", ActualOccupant = "Stall Tenant", NameOnContract = "Stall Tenant",
                                EffectivityDate = new DateOnly(2026, 1, 1), DurationYears = 3, MonthlyRentalRate = 2_000m,
                                ActualMonthlyRental = 2_000m, WholeYearRental = 24_000m,
                            },
                        ],
                    },
                ],
            }));
        stalls.Setup(s => s.GetStallHoldersListAsync(It.Is<FacilityCode>(c => c != FacilityCode.NCC), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<StallHoldersListDto>.Success(new StallHoldersListDto()));
        Services.AddSingleton(stalls.Object);

        _obligations.Setup(o => o.GetAccountsAsync(It.IsAny<ObligationKind>()))
            .ReturnsAsync((ObligationKind kind) => _failKanmanggay && kind == ObligationKind.KanmanggaySpaceRental
                ? Result<IReadOnlyList<ObligationAccountDto>>.Failure("Offline")
                : Result<IReadOnlyList<ObligationAccountDto>>.Success(accounts.Where(a => a.Kind == kind).ToList()));
        Services.AddSingleton(_obligations.Object);

        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([
                new(FacilityCode.NCC, "New Commercial Center", "NCC", 0, null, null, null)]));
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<StallHolderList>();
    }

    private static readonly ObligationAccountDto[] Both =
    [
        Space(ObligationKind.VegetableFruitSpaceRental, "2", "Veg Seller", OccupancyArrangement.SignedContract, 600m, reference: "VF-2", contractName: "Veg Seller"),
        Space(ObligationKind.VegetableFruitSpaceRental, "10", "Space Only Seller", OccupancyArrangement.SpaceOnly, 450m),
        Space(ObligationKind.VegetableFruitSpaceRental, "3", "Gone Seller", OccupancyArrangement.SignedContract, 700m, to: new DateOnly(2026, 1, 31)),
        Space(ObligationKind.KanmanggaySpaceRental, "5", "Kanmanggay Seller", OccupancyArrangement.SignedContract, 800m, reference: "KM-5", contractName: "Kanmanggay Seller"),
    ];

    private static void ChooseSource(IRenderedComponent<StallHolderList> cut, string label)
    {
        cut.Find(".sh-bar .fh-dd-trigger").Click();
        cut.FindAll(".fh-dd-item").Single(i => i.TextContent.Trim() == label).Click();
    }

    private static string[] SourceChoices(IRenderedComponent<StallHolderList> cut)
    {
        cut.Find(".sh-bar .fh-dd-trigger").Click();
        var items = cut.FindAll(".fh-dd-item").Select(i => i.TextContent.Trim()).ToArray();
        cut.Find(".sh-bar .fh-dd-trigger").Click();
        return items;
    }

    private static void Choose(IRenderedComponent<StallHolderList> cut, string label) =>
        cut.FindAll("button.sh-chip").First(b => b.TextContent.Trim() == label).Click();

    [Fact]
    public void Register_has_one_coordinated_toolbar_for_filters_exports_and_year()
    {
        var cut = Render(Both);
        var bar = cut.Find(".sh-bar");
        Assert.Equal(3, bar.QuerySelectorAll(".sh-filter button.sh-chip").Length);
        Assert.NotNull(bar.QuerySelector(".fh-dd-trigger"));
        Assert.NotNull(bar.QuerySelector(".sh-search input[type=search]"));
        var exports = bar.QuerySelectorAll(".sh-export .sh-split-part");
        Assert.Equal(4, exports.Length);
        Assert.Contains("Print", exports[0].TextContent);
        Assert.Contains("Excel", exports[1].TextContent);
        Assert.Contains("CSV", exports[2].TextContent);
        Assert.Contains("Save as PDF", exports[3].TextContent);
        Assert.NotNull(bar.QuerySelector(".sh-year-nav .rpt-date-nav"));
    }

    [Fact]
    public void The_page_is_the_Stall_and_Space_Holder_Register_on_the_same_route()
    {
        var cut = Render(Both);

        Assert.Equal("Stall & Space Holder Register", cut.Find(".topbar-title").TextContent);
        Assert.Equal("Stall & Space Holder Register", cut.Find(".breadcrumb-active").TextContent);
        cut.WaitForAssertion(() => Assert.Equal("Stall & Space Holder Register", cut.Find(".sh-doc").TextContent));
        Assert.DoesNotContain("List of Stallholders", cut.Markup);
        var route = typeof(StallHolderList).GetCustomAttributes<Microsoft.AspNetCore.Components.RouteAttribute>().Single();
        Assert.Equal("/stall-holders", route.Template);
    }

    [Fact]
    public void All_holders_state_each_source_on_its_own_and_sum_no_rate_across_sources()
    {
        var cut = Render(Both);

        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));
        Assert.Contains("Stall Tenant", cut.Markup);
        Assert.Contains("Veg Seller", cut.Markup);
        // Each source states its own total; the summary row counts holders and never adds a rate.
        Assert.Contains("Active Stalls", cut.Find(".sh-summary").TextContent);
        Assert.Contains("Active Spaces", cut.Find(".sh-summary").TextContent);
        Assert.DoesNotContain("Total Monthly Rate", cut.Markup);
        Assert.DoesNotContain("Whole-Year", cut.Find(".sh-summary").TextContent);
        // Vegetable / Fruits: two active spaces (600 + 450); the closed account is not a current holder.
        Assert.Contains("₱1,050.00", cut.Markup);
        Assert.DoesNotContain("Gone Seller", cut.Markup);
    }

    [Fact]
    public void A_space_without_a_signed_contract_says_so_and_invents_no_particulars()
    {
        var cut = Render(Both);

        cut.WaitForAssertion(() => Assert.Contains("Space Only Seller", cut.Markup));
        var row = cut.FindAll("tr").Single(r => r.TextContent.Contains("Space Only Seller"));
        Assert.Contains("No contract (space only)", row.TextContent);
        Assert.Equal(5, int.Parse(row.QuerySelector(".sh-nc")!.GetAttribute("colspan")!));
    }

    [Fact]
    public void Space_rentals_filter_hides_stalls_and_the_source_selector_narrows_it()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        Choose(cut, "Space rentals");
        Assert.DoesNotContain("Stall Tenant", cut.Markup);
        Assert.Contains("Veg Seller", cut.Markup);
        Assert.Contains("Kanmanggay Seller", cut.Markup);

        ChooseSource(cut, "Kanmanggay");
        Assert.DoesNotContain("Veg Seller", cut.Markup);
        Assert.Contains("Kanmanggay Seller", cut.Markup);
        Assert.Equal("KANMANGGAY", cut.Find(".sh-doc-scope").TextContent);
    }

    [Fact]
    public void Stall_rentals_filter_hides_the_space_sources()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        Choose(cut, "Stall rentals");

        Assert.Contains("Stall Tenant", cut.Markup);
        Assert.DoesNotContain("Veg Seller", cut.Markup);
        Assert.DoesNotContain("Kanmanggay Seller", cut.Markup);
    }

    [Fact]
    public void Search_reaches_space_holders_by_occupant_contract_reference_and_space_number()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        cut.Find("input[type=search]").Input("KM-5");
        Assert.Contains("Kanmanggay Seller", cut.Markup);
        Assert.DoesNotContain("Veg Seller", cut.Markup);
        Assert.DoesNotContain("Stall Tenant", cut.Markup);
    }


    private static ObligationAccountDto EventLot(string lot, string occupant, LotRentalEvent ev, DateOnly date, decimal amount,
        OccupancyArrangement arrangement = OccupancyArrangement.SpaceOnly, string? location = null) =>
        new(Guid.NewGuid(), ObligationKind.FiestaArawLotRental, "Fiesta / Araw", Guid.NewGuid(), occupant, null, null, lot, ev, date,
            date, null, amount, null, 0m, 0m, 0m, arrangement, null, occupant, null, null, null, null, location);

    private static readonly int ThisYear = DateTime.Now.Year;

    [Fact]
    public void Fiesta_and_Araw_event_lots_are_listed_with_their_event_date_and_amount_and_no_invented_contract()
    {
        var cut = Render(Both.Append(EventLot("4", "Fiesta Seller", LotRentalEvent.Fiesta, new DateOnly(ThisYear, 5, 12), 1_500m, location: "Plaza corner")).ToArray());

        cut.WaitForAssertion(() => Assert.Contains("Fiesta Seller", cut.Markup));
        var row = cut.FindAll("tr").Single(r => r.TextContent.Contains("Fiesta Seller"));
        Assert.Contains("Fiesta", row.TextContent);
        Assert.Contains($"5/12/{ThisYear}", row.TextContent);
        Assert.Contains("Plaza corner", row.TextContent);
        Assert.Contains("₱1,500.00", row.TextContent);
        Assert.Contains("No contract (space only)", row.TextContent);   // the source's own basis, not a lease it does not hold
        Assert.Contains("Event lots", cut.Markup);
        // An event fee is not a rent: no monthly total or whole-year figure is worked out for the group.
        var group = cut.FindAll(".sh-facility").Single(f => f.TextContent.Contains("Fiesta Seller"));
        Assert.DoesNotContain("monthly", group.QuerySelector(".sh-fac-stats")!.TextContent);
        Assert.Contains("1 lot in", group.QuerySelector(".sh-fac-stats")!.TextContent);
        _obligations.Verify(o => o.GetAccountsAsync(ObligationKind.FiestaArawLotRental), Times.Once);
    }

    [Fact]
    public void Event_lots_belong_to_the_year_of_their_event_and_are_never_listed_twice()
    {
        var thisYear = EventLot("1", "This Year Seller", LotRentalEvent.Araw, new DateOnly(ThisYear, 8, 1), 900m);
        var lastYear = EventLot("2", "Last Year Seller", LotRentalEvent.Fiesta, new DateOnly(ThisYear - 1, 5, 12), 800m);
        var cut = Render(thisYear, lastYear);

        cut.WaitForAssertion(() => Assert.Contains("This Year Seller", cut.Markup));
        Assert.DoesNotContain("Last Year Seller", cut.Markup);
        Assert.Single(cut.FindAll("tr").Where(r => r.TextContent.Contains("This Year Seller")));

        cut.Find("button[aria-label='Previous period']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Last Year Seller", cut.Markup));
        Assert.DoesNotContain("This Year Seller", cut.Markup);
    }

    [Fact]
    public void Event_lots_export_as_event_based_with_their_event_columns_and_no_rent_figures()
    {
        var cut = Render(EventLot("4", "Fiesta Seller", LotRentalEvent.Fiesta, new DateOnly(ThisYear, 5, 12), 1_500m));
        cut.WaitForAssertion(() => Assert.Contains("Fiesta Seller", cut.Markup));

        var lines = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

        Assert.EndsWith("Status,Event,Event Date,Event Amount,Location / Note", lines[0]);
        var line = Assert.Single(lines, l => l.Contains("Fiesta Seller"));
        Assert.Contains("Event lot", line);
        Assert.Contains("Event-based", line);
        Assert.Contains($"5/12/{ThisYear}", line);
        Assert.Contains("1500.00", line);
        Assert.DoesNotContain("Monthly space rental", line);
    }

    [Fact]
    public void The_CSV_is_grouped_by_source_and_matches_what_is_on_screen()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        var href = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!);
        var lines = href.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

        Assert.StartsWith("data:text/csv;charset=utf-8,Source,Holding,Section,", lines[0]);
        Assert.Contains(lines, l => l.Contains("Stall rental") && l.Contains("Stall Tenant"));
        Assert.Contains(lines, l => l.Contains("Space rental") && l.Contains("Veg Seller") && l.Contains("VF-2"));
        Assert.Contains(lines, l => l.Contains("Space rental") && l.Contains("No contract (space only)"));
        Assert.DoesNotContain(lines, l => l.Contains("Gone Seller"));
        // Header + one stall + three active spaces.
        Assert.Equal(5, lines.Count);
        Assert.Contains("stall-space-holders_all_", cut.Find("a[download]").GetAttribute("download"));
    }

    [Fact]
    public void An_earlier_year_leaves_the_space_rentals_out_and_says_why()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        cut.Find("button[aria-label='Previous period']").Click();

        cut.WaitForAssertion(() => Assert.Contains("current register and are not shown for an earlier year", cut.Markup));
        Assert.DoesNotContain("Kanmanggay Seller", cut.Markup);
        Assert.DoesNotContain("Veg Seller", cut.Markup);
    }

    [Fact]
    public void A_failed_space_read_is_stated_with_a_retry_and_the_stalls_still_show()
    {
        _failKanmanggay = true;
        var cut = Render(Both);

        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay space holders could not be loaded", cut.Find("[role=alert]").TextContent));
        Assert.NotNull(cut.Find("button.sh-retry"));
        Assert.Contains("Stall Tenant", cut.Markup);
        Assert.Contains("Veg Seller", cut.Markup);
        Assert.DoesNotContain("Kanmanggay Seller", cut.Markup);
    }

    [Fact]
    public void Filtering_is_one_control_band_with_no_second_row_of_source_chips()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        var bar = cut.Find(".sh-bar");
        Assert.Equal(["All holders", "Stall rentals", "Space rentals"], bar.QuerySelectorAll(".sh-chip").Select(c => c.TextContent.Trim()).ToArray());
        Assert.NotNull(bar.QuerySelector(".fh-dd"));
        Assert.NotNull(bar.QuerySelector("input[type=search]"));
        Assert.Empty(cut.FindAll(".sh-controls-scope"));
    }

    [Fact]
    public void The_source_choices_follow_the_kind_of_holder()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        cut.WaitForAssertion(() => Assert.Equal(
            ["All sources", "New Commercial Center", "Vegetable / Fruits", "Kanmanggay", "Fiesta / Araw"],
            SourceChoices(cut)));

        Choose(cut, "Stall rentals");
        cut.WaitForAssertion(() => Assert.Equal(
            ["All facilities", "New Commercial Center"], SourceChoices(cut)));

        Choose(cut, "Space rentals");
        cut.WaitForAssertion(() => Assert.Equal(
            ["All spaces", "Vegetable / Fruits", "Kanmanggay", "Fiesta / Araw"], SourceChoices(cut)));
    }

    [Fact]
    public void A_space_source_chosen_under_all_holders_shows_only_that_source()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        ChooseSource(cut, "Kanmanggay");

        Assert.Contains("Kanmanggay Seller", cut.Markup);
        Assert.DoesNotContain("Stall Tenant", cut.Markup);
        Assert.DoesNotContain("Veg Seller", cut.Markup);
        Assert.Equal("KANMANGGAY", cut.Find(".sh-doc-scope").TextContent);
        Assert.Contains("spaceholders_kanmanggay", cut.Find("a[download]").GetAttribute("download"));
    }

    [Fact]
    public void Print_Excel_CSV_and_Save_as_PDF_are_one_joined_group_that_never_wraps_a_label()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        var group = cut.Find(".sh-export");
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal(["Print", "Excel", "CSV", "Save as PDF"], group.QuerySelectorAll(".sh-split-part").Select(p => p.TextContent.Trim()).ToArray());
        Assert.Equal(1, group.QuerySelectorAll("a[download]").Length);
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EEMOCantilanSDS.Client", "Components", "Pages", "Reports", "StallHolderList.razor.css"));
        Assert.Contains("white-space: nowrap", css[css.IndexOf(".sh-export .sh-print-btn", StringComparison.Ordinal)..]);
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }

    [Fact]
    public void The_summary_grid_fits_the_cards_it_has_and_the_skeleton_matches()
    {
        var cut = Render(Both);
        cut.WaitForAssertion(() => Assert.Contains("Kanmanggay Seller", cut.Markup));

        Assert.Equal(3, cut.FindAll(".sh-summary .sh-sum-card").Count);   // Active Stalls, Active Spaces, Sources — no placeholder
        var css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EEMOCantilanSDS.Client", "Components", "Pages", "Reports", "StallHolderList.razor.css"));
        Assert.Contains(".sh-summary { grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }", css);
        var razor = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EEMOCantilanSDS.Client", "Components", "Pages", "Reports", "StallHolderList.razor"));
        Assert.Contains("for (var i = 0; i < 3; i++)", razor);
    }
}
