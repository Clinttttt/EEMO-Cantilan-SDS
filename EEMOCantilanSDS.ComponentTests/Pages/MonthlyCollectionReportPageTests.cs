using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using MonthEndReport = EEMOCantilanSDS.Client.Components.Pages.Reports.MonthEndReport;

/// <summary>
/// The Monthly Collection Report: organized by the office's revenue groups from the official source projection, with the
/// facility aggregation kept as account-level detail and reconciled to it.
/// </summary>
public sealed class MonthlyCollectionReportPageTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IFacilitiesApiClient> _facilities = new();
    private readonly Mock<IOfficialReportsApiClient> _official = new();

    public MonthlyCollectionReportPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_facilities.Object);
        Services.AddSingleton(_official.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");
    }

    private static MonthEndFacilityDto Npm() => new(FacilityCode.NPM, "New Public Market", true, 5000m, 800m, 86, 3, 1, 2, 6,
        [new("1", "Maria Santos", 900m, "Paid", 900m, 0m, "OR-1"), new("2", "Ben Cruz", 900m, "Unpaid", 0m, 800m, null)], []);

    private static MonthEndFacilityDto Slaughter() => new(FacilityCode.SLH, "Slaughterhouse", false, 330m, 0m, 100, 0, 0, 0, 1, [],
        [new("Lisa Reyes", 1, 330m, [new("Oct 3", "2 Hog · ₱165/head", 330m, "OR-9")], "2 Hog", 2)]);

    private static MonthEndFacilityDto TerminalTrips() => new(FacilityCode.TRM, "Transport Terminal", false, 500m, 0m, 100, 0, 0, 0, 1, [],
        [new("Driver A", 1, 500m, [new("Oct 4", "Trip #1", 500m, "OR-4")], null, 1)]);

    private static RevenueSourcePerformanceRowDto Row(string key, string label, string group, string groupLabel, string model, decimal collected,
        int? count = 1, decimal legacy = 0m, bool awaiting = false) =>
        new(key, label, group, groupLabel, model, "OR", null, collected, legacy, collected - legacy, count, null, null, "Active", awaiting);

    // Official total with no Arrears: 1,000 + 120 + 5,000 + 900 + 900 + 330 + 250 = 8,500.
    private static RevenueSourcePerformanceRowDto[] OfficialRows(decimal arrears = 0m) =>
    [
        Row("MARKET_FEES", "Market Fees", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 1000m, 40),
        Row("TABO", "Tabo", "MARKET", "Income From Market", RevenueSourceModel.Transactional, 120m, 6),
        Row("RENT_NPM", "New Public Market (NPM)", "RENT", "Rent Income", RevenueSourceModel.RecurringObligation, 5000m, 100),
        Row("KANMANGGAY_SPACE_RENTAL", "Kanmanggay", "SPACE", "Space Rental", RevenueSourceModel.RecurringObligation, 900m, 11),
        Row("TERMINAL_COMFORT_ROOM", "COMFORT ROOM", "TERMINAL", "Income From Terminal", RevenueSourceModel.Transactional, 900m, null, legacy: 500m),
        Row("SLAUGHTERHOUSE", "Income from Slaughterhouse", "SLAUGHTERHOUSE", "Income From Slaughterhouse", RevenueSourceModel.QuantityService, 330m, null, legacy: 330m),
        Row("PENALTIES_AND_FINES", "Fines", "OTHER", "Other operations", RevenueSourceModel.Transactional, 250m, 4),
        Row("ARREARS", "Arrears", "RECEIVABLE", "Receivables context", RevenueSourceModel.Receivable, arrears, null),
    ];

    private static RevenueSourcePerformanceDto Official(params RevenueSourcePerformanceRowDto[] rows) =>
        new(PhilippineTime.Now.Year, PhilippineTime.Now.Month, [], rows, rows.Sum(r => r.Collected), [], DateTime.UtcNow);

    private void Serve(RevenueSourcePerformanceDto? official)
    {
        MonthEndFacilityDto[] facilities = [Npm(), Slaughter(), TerminalTrips()];
        var detail = new MonthEndReportDto(PhilippineTime.Now.Year, PhilippineTime.Now.Month, "October 2026",
            facilities.Sum(f => f.Collected), 800m, 86, 3, 1, 2, facilities);
        _facilities.Setup(x => x.GetMonthEndReportAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<MonthEndReportDto>.Success(detail));
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>())).ReturnsAsync(
            official is null ? Result<RevenueSourcePerformanceDto>.Failure("offline") : Result<RevenueSourcePerformanceDto>.Success(official));
    }

    private IRenderedComponent<MonthEndReport> RenderReport()
    {
        var cut = RenderComponent<MonthEndReport>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".me-sheet")), Timeout);
        return cut;
    }

    private static void Choose(IRenderedComponent<MonthEndReport> cut, string caption, string label)
    {
        cut.FindAll(".fh-dd").Single(d => d.QuerySelector(".fh-dd-cap")?.TextContent.Trim() == caption).QuerySelector(".fh-dd-trigger")!.Click();
        cut.FindAll(".fh-dd-item").Single(i => i.TextContent.Trim() == label).Click();
    }

    private static string Card(IRenderedComponent<MonthEndReport> cut, string label) =>
        cut.FindAll(".me-sum-card").Single(c => c.QuerySelector(".me-sum-lbl")!.TextContent.Trim() == label).QuerySelector(".me-sum-val")!.TextContent.Trim();

    private static string[] SourceLabels(IRenderedComponent<MonthEndReport> cut) =>
        cut.FindAll(".me-official .me-src-table tbody tr:not(.me-area-row):not(.me-area-sub) td:first-child").Select(c => c.TextContent.Trim()).ToArray();

    [Fact]
    public void CustomSpacesAreLabeledInAccountDetailWithoutMovingTheOfficialPendingRevenue()
    {
        var custom = new MonthEndFacilityDto(FacilityCode.Custom1, "Civic Plaza", true,
            300m, 0m, 100, 1, 0, 0, 1,
            [new("S-01", "Ana Cruz", 300m, "Paid", 300m, 0m, "OR-123")], [],
            CustomCategory: CustomFacilityCategory.SpaceRental);
        var details = new MonthEndReportDto(PhilippineTime.Now.Year, PhilippineTime.Now.Month, "October 2026",
            300m, 0m, 100, 1, 0, 0, [custom]);
        _facilities.Setup(x => x.GetMonthEndReportAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<MonthEndReportDto>.Success(details));
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(Official(
                Row("RENT_OTHER", "Other rental facilities", "PENDING",
                    "Awaiting an approved official grouping", RevenueSourceModel.RecurringObligation, 300m))));
        var cut = RenderReport();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Civic Plaza", cut.Markup);
            Assert.Contains("Space Rental", cut.FindAll(".me-fac-head").Single(h => h.TextContent.Contains("Civic Plaza")).TextContent);
            Assert.Equal(EEMOCantilanSDS.Application.Common.Revenue.OfficialMonthlyIncomeStructure.Pending,
                EEMOCantilanSDS.Application.Common.Revenue.OfficialMonthlyIncomeStructure.Rows.Single(r => r.Key == "RENT_OTHER").GroupKey);
            Assert.Equal("RENT_OTHER", EEMOCantilanSDS.Application.Common.Revenue.OfficialMonthlyIncomeStructure.RowKeyFor(
                EEMOCantilanSDS.Domain.Constants.RevenueClassificationCodes.PermanentStallRent, FacilityCode.Custom1));
        }, Timeout);
    }

    [Fact]
    public void TheReportIsOrganizedByTheOfficesGroups_AndItsTotalIsTheOfficialTotal()
    {
        Serve(Official(OfficialRows()));

        var cut = RenderReport();

        var groups = cut.FindAll(".me-src-table tbody tr.me-area-row").Select(r => r.TextContent.Trim()).ToArray();
        // Fines now read under Space Rental, so nothing is left for Others and it is not shown as an empty band.
        Assert.Equal(["Income From Market", "Rent Income — Stall Rental", "Space Rental", "Income From Terminal", "Income From Slaughterhouse"], groups);
        Assert.Equal("₱8,500.00", cut.Find(".me-src-table tfoot td.me-num").TextContent.Trim());
        Assert.Contains("Total — all sources", cut.Find(".me-src-table tfoot").TextContent);
        Assert.Equal("₱8,500.00", Card(cut, "Collected · all sources"));
        Assert.Equal("Account detail", cut.Find(".me-section-title").TextContent.Trim());
        // A source with no count shows a dash, never a partial or invented one.
        Assert.Equal("—", cut.FindAll(".me-src-table tbody tr").Single(r => r.TextContent.Contains("COMFORT ROOM")).QuerySelectorAll("td.me-num")[0].TextContent.Trim());
        _official.Verify(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public void ArrearsWithNothingRecoveredIsNotListedAsALine()
    {
        Serve(Official(OfficialRows(arrears: 0m)));

        var cut = RenderReport();

        Assert.DoesNotContain(SourceLabels(cut), label => label.StartsWith("Arrears"));
    }

    [Fact]
    public void FinesReadUnderSpaceRental_AndRecoveredArrearsUnderRentIncome_AsOnTheOfficesSheet()
    {
        Serve(Official(OfficialRows(arrears: 1200m)));

        var cut = RenderReport();

        var arrears = cut.FindAll(".me-src-table tbody tr").Single(r => r.TextContent.StartsWith("Arrears"));
        Assert.Contains("₱1,200.00", arrears.TextContent);
        Assert.Contains("Recovery", arrears.TextContent);

        // Each source sits under the group the office's sheet prints it in, and no "listed under Others" caveat is needed.
        string GroupOf(string label)
        {
            var rows = cut.FindAll(".me-src-table tbody tr").ToList();
            var at = rows.FindIndex(r => r.TextContent.StartsWith(label));
            return rows.Take(at).Last(r => r.ClassList.Contains("me-area-row")).TextContent.Trim();
        }
        Assert.Equal("Space Rental", GroupOf("Fines"));
        Assert.Equal("Rent Income — Stall Rental", GroupOf("Arrears"));
        Assert.Empty(cut.FindAll(".me-fine-print"));
        // Placement only: the headline is still the official total, with the Arrears recovery included once.
        Assert.Equal("₱9,700.00", cut.Find(".me-src-table tfoot td.me-num").TextContent.Trim());
    }

    [Fact]
    public void GroupAndSourceNarrowTheOfficialRowsAndTheAccountDetailTogether()
    {
        Serve(Official(OfficialRows()));
        var cut = RenderReport();

        Choose(cut, "Group", "Income From Slaughterhouse");

        Assert.Equal(["Income from Slaughterhouse"], SourceLabels(cut));
        Assert.Equal(["Slaughterhouse"], cut.FindAll(".me-fac-name").Select(n => n.TextContent.Trim()).Where(n => n != "Collections by revenue source" && n != "Reconciliation to the official collections").ToArray());
        Assert.Equal("₱330.00", Card(cut, "Collected · selected scope"));
        Assert.Contains("Income From Slaughterhouse", cut.Find(".me-scope").TextContent);

        Choose(cut, "Group", "Income From Market");
        cut.FindAll(".fh-dd").Single(d => d.QuerySelector(".fh-dd-cap")!.TextContent.Trim() == "Source").QuerySelector(".fh-dd-trigger")!.Click();
        Assert.Equal(["All in group", "Market Fees", "Tabo"], cut.FindAll(".fh-dd-item").Select(i => i.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void ASingleSourceShowsItsOwnRowAndOnlyItsOwnDetail()
    {
        Serve(Official(OfficialRows()));
        var cut = RenderReport();
        Choose(cut, "Group", "Income From Market");

        Choose(cut, "Source", "Tabo");

        Assert.Equal(["Tabo"], SourceLabels(cut));
        Assert.Equal("₱120.00", Card(cut, "Collected · selected scope"));
        Assert.Contains("Account-level detail is not kept for this scope", cut.Markup);   // Tabo's facility is not in this report's detail
    }

    [Fact]
    public void ADirectServiceScopeStatesNoOutstandingRateOrPaidPartialUnpaid_ARentalScopeDoes()
    {
        Serve(Official(OfficialRows()));
        var cut = RenderReport();
        Assert.Equal("₱800.00", Card(cut, "Outstanding rent"));
        Assert.DoesNotContain(cut.FindAll(".me-sum-card"), c => c.TextContent.Contains("Rental collection rate"));   // never an all-source rate

        Choose(cut, "Group", "Space Rental");

        Assert.DoesNotContain(cut.FindAll(".me-sum-card"), c => c.TextContent.Contains("Outstanding") || c.TextContent.Contains("rate") || c.TextContent.Contains("paid / partial"));
        Assert.Contains("Account-level detail is not kept for this scope", cut.Markup);
        Assert.Equal("₱1,150.00", Card(cut, "Collected · selected scope"));   // Kanmanggay 900 + Fines 250, as the office's sheet groups them

        Choose(cut, "Group", "Rent Income — Stall Rental");
        Assert.Equal("86%", Card(cut, "Rental collection rate"));
        Assert.Equal("3 / 1 / 2", Card(cut, "Rental accounts paid / partial / unpaid"));
    }

    [Fact]
    public void SearchIsAboutAccounts_SoItHidesTheOfficialSectionAndSumsOnlyTheMatchingDetail()
    {
        Serve(Official(OfficialRows()));
        var cut = RenderReport();

        cut.Find("input[type='search']").Input("maria");

        Assert.Empty(cut.FindAll(".me-src-table"));
        Assert.Contains("Maria Santos", cut.Markup);
        Assert.DoesNotContain("Ben Cruz", cut.Markup);
        Assert.Equal("₱900.00", Card(cut, "Collected · matching accounts"));
        Assert.Contains("matching “maria”", cut.Find(".me-scope").TextContent);
    }

    [Fact]
    public void WhenTheOfficialReadFailsTheSheetSaysSo_KeepsTheDetail_AndRetryRecovers()
    {
        Serve(official: null);
        var cut = RenderReport();

        Assert.Contains("could not be loaded", cut.Find(".me-official [role='alert']").TextContent);
        Assert.Empty(cut.FindAll(".me-src-table"));
        Assert.DoesNotContain("Total — all sources", cut.Markup);
        Assert.Contains("Maria Santos", cut.Markup);
        Assert.Equal("₱5,830.00", Card(cut, "Collected · account detail only"));
        Assert.Empty(cut.FindAll(".me-recon"));

        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(Official(OfficialRows())));
        cut.Find(".me-official .me-retry").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".me-src-table")), Timeout);
        Assert.Equal("₱8,500.00", Card(cut, "Collected · all sources"));
    }

    [Fact]
    public void TerminalTripsAreLabelledAsLegacyRecords_AndReconcileOnlyToTheLegacyPartOfTheOfficialTerminalRows()
    {
        Serve(Official(OfficialRows()));
        var cut = RenderReport();

        var trips = cut.FindAll(".me-facility").Single(f => f.TextContent.Contains("Transport Terminal") && f.QuerySelector(".me-tag") is not null);
        Assert.Contains("Legacy trip records", trips.QuerySelector(".me-tag")!.TextContent);
        Assert.DoesNotContain("rate", trips.QuerySelector(".me-fac-stats")!.TextContent);       // a rate is stated only for rentals

        var row = cut.FindAll(".me-recon tbody tr").Single(r => r.TextContent.Contains("legacy trip records"));
        Assert.Equal(["₱500.00", "₱500.00", "—"], row.QuerySelectorAll("td.me-num").Select(c => c.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void ADifferenceBetweenDetailAndOfficialIsStatedNotAbsorbed_AndTheHeadlineIsFullyAccountedFor()
    {
        var rows = OfficialRows().Select(r => r.Key == "RENT_NPM" ? r with { Collected = 4900m, CanonicalCollected = 4900m } : r).ToArray();
        Serve(Official(rows));

        var cut = RenderReport();

        Assert.Contains("Differences shown", cut.Find(".me-recon .me-fac-stats").TextContent);
        var npm = cut.FindAll(".me-recon tbody tr").Single(r => r.TextContent.StartsWith("New Public Market"));
        Assert.Equal(["₱5,000.00", "₱4,900.00", "+₱100.00"], npm.QuerySelectorAll("td.me-num").Select(c => c.TextContent.Trim()).ToArray());
        var foot = cut.Find(".me-recon tfoot");
        Assert.Contains("Itemised by account detail", foot.TextContent);
        Assert.Contains("Stated by source total only", foot.TextContent);
        // 4,900 + 330 + 500 legacy itemised; the rest of the 8,400 official total is source totals only.
        Assert.Equal(["₱5,730.00", "₱2,670.00", "₱8,400.00"], foot.QuerySelectorAll("td.me-num").Select(c => c.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void TheScreenHeaderDropsTheOfficeAcronym_AndTheOfficialDocumentHeadingStays()
    {
        Serve(Official(OfficialRows()));
        var cut = RenderReport();

        var crumb = cut.Find(".topbar-breadcrumb").TextContent;
        Assert.Contains("Dashboard", crumb);
        Assert.Contains("Monthly Collection Report", crumb);
        Assert.DoesNotContain("Admin", crumb);
        Assert.Equal("Monthly Collection Report", cut.Find(".me-doc").TextContent.Trim());
        Assert.Contains("Republic of the Philippines", cut.Find(".me-rpt-head").TextContent);
    }

    [Fact]
    public void RendersForVisualReview()
    {
        var rows = OfficialRows(arrears: 1200m).Select(r => r.Key == "RENT_NPM" ? r with { Collected = 4900m, CanonicalCollected = 4900m } : r).ToArray();
        Serve(Official(rows));
        var cut = RenderReport();
        Export("monthly-report", cut.Markup);
    }

    internal static void Export(string name, string markup)
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
    public void PrintableStylesKeepTheNewSectionsTogether_AndTheStylesheetIsBalanced()
    {
        var css = File.ReadAllText("../../../../EEMOCantilanSDS.Client/Components/Pages/Reports/MonthEndReport.razor.css");
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
        Assert.Contains(".me-facility { break-inside: avoid; }", css);       // official, detail and reconciliation sections are all .me-facility
        Assert.Contains("repeat(auto-fit, minmax(170px, 1fr))", css);
    }
}
