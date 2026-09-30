using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// ONE SOURCE OWNER: NPM owns the Fish / Meat stalls and weighing records. Fish / Meat Vendor Fees and Weight &amp;
/// Measure are focused, read-only financial views over that source — they add no vendor, stall, collector, payment or
/// weighing action, and they never take a figure from stall rent.
/// </summary>
public sealed class FishMeatWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid FishStallId = Guid.NewGuid();
    private static readonly string[] ForbiddenActions =
        ["Add Vendor", "Add vendor", "Assign", "Collect", "Post", "Record", "Issue OR", "Enter kilos", "Save"];

    private readonly Mock<IFacilitiesApiClient> _facilities = new();
    private readonly Mock<IStallsApiClient> _stalls = new();

    public FishMeatWorkspaceTests()
    {
        _facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                new[] { new FacilitySidebarSummaryDto(FacilityCode.NPM, "Tenant Public Market", "TPM", 0) }));
        _stalls.Setup(x => x.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Success(new NpmRatesDto(30m, 1m)));

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(_facilities.Object);
        Services.AddSingleton(_stalls.Object);
        Services.AddSingleton<FacilityState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(typeof(FishMeatVendorFees), "/operations/fish-meat-vendor-fees")]
    [InlineData(typeof(WeightAndMeasure), "/operations/weight-and-measure")]
    public void Routes_AreOperationsWorkspaces_ForHeadAndAdmin(Type page, string template)
    {
        Assert.Equal(template, Assert.Single(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(page.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public void VendorFees_StatesTheObligationIsUnavailable_WithoutRentFiguresOrManagementActions()
    {
        var cut = RenderComponent<FishMeatVendorFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            Assert.Contains("Fish / Meat Vendor Fees", Assert.Single(cut.FindAll("h1")).TextContent);
            Assert.Contains("Official Receipt", cut.Find("header").TextContent);
            Assert.Contains("aren't available yet", cut.Markup);

            // No amounts at all, and in particular nothing that could have come from stall rent.
            Assert.DoesNotContain("₱", cut.Markup);
            Assert.Empty(cut.FindAll("input"));
            Assert.Empty(cut.FindAll("form"));
            AssertNoManagementActions(cut);

            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/facility/npm" && a.TextContent.Contains("Tenant Public Market"));
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/weight-and-measure");
        }, Timeout);

        _stalls.Verify(x => x.GetNpmRatesAsync(), Times.Never);
    }

    [Fact]
    public void WeightAndMeasure_ListsNpmFishWeighing_WithSourceLinks_AndServerTotalsOnly()
    {
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(fishFeeAmount: 37m, meatKilos: 4m, meatAmount: 120m, fishFrozen: 15m, fishUnfrozenKilos: 10m,
                Stall(FishStallId, "F-12", "Juan Dela Cruz", "Fish Area", fishKilos: 25m),
                Stall(Guid.NewGuid(), "V-01", "Rosa Lim", "Vegetable Area", fishKilos: 0m))));

        var cut = RenderComponent<WeightAndMeasure>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            Assert.Contains("Official Receipt", cut.Find("header").TextContent);

            var row = Assert.Single(cut.FindAll("[aria-label='Fish weighing by stall'] tbody tr"));
            Assert.Contains("Juan Dela Cruz", row.TextContent);
            Assert.Contains("Stall F-12", row.TextContent);
            Assert.Contains("25.00 kg", row.TextContent);
            Assert.Equal($"/profile/npm/{FishStallId}", row.QuerySelector("a")!.GetAttribute("href"));
            Assert.DoesNotContain("Rosa Lim", cut.Markup);

            // The report's fish amount (₱37) is kilos x the rate in force when it is READ, so it is never shown as collected
            // money. Only the server's frozen Fish total is shown, and kilos without frozen rate evidence are stated as such.
            var summary = cut.Find("dl[aria-label='Weighing position']").TextContent;
            Assert.DoesNotContain("₱37.00", cut.Markup);
            Assert.Contains("Fish weighing (frozen)", summary);
            Assert.Contains("₱15.00", summary);                 // frozen Fish weighing money from the server
            Assert.Contains("Fish kilos without rate evidence", summary);
            Assert.Contains("10.00 kg", summary);               // kilos with no frozen rate stay unresolved

            // Meat weighing money is the amount frozen on each collection, as the server totals it.
            Assert.Contains("₱120.00", summary);
            var meat = cut.Find("dl.wm-meat-facts").TextContent;
            Assert.Contains("4.00 kg", meat);
            Assert.Contains("₱120.00", meat);
            Assert.Empty(cut.FindAll("form"));
            Assert.DoesNotContain(cut.FindAll("input"), i => i.GetAttribute("type") == "number");
            AssertNoManagementActions(cut);
        }, Timeout);
    }

    [Fact]
    public void WeightAndMeasure_WithNoMeatWeighing_SaysSo()
    {
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(fishFeeAmount: 0m)));

        var cut = RenderComponent<WeightAndMeasure>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No Meat weighing was recorded", cut.Markup);
            Assert.Empty(cut.FindAll("dl.wm-meat-facts"));
        }, Timeout);
    }

    [Fact]
    public void WeightAndMeasure_FailedSource_SaysSo_InsteadOfShowingZeroes()
    {
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Failure("offline"));

        var cut = RenderComponent<WeightAndMeasure>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Weighing records are unavailable.", cut.Markup);
            Assert.DoesNotContain("₱0.00", cut.Find("dl[aria-label='Weighing position']").TextContent);
        }, Timeout);
    }

    private static void AssertNoManagementActions<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        foreach (var control in cut.FindAll("button, a"))
        {
            var text = control.TextContent.Trim();
            Assert.DoesNotContain(ForbiddenActions, action => text.StartsWith(action, StringComparison.Ordinal));
        }
    }

    private static StallComplianceDto Stall(Guid id, string stallNo, string occupant, string section, decimal fishKilos) => new(
        id, stallNo, occupant, occupant, section, "Daily", 900m, 30m, "Paid", 0m, 0m, null, 0, 0d, null, 1, 0m,
        FishKilos: fishKilos);

    private static FacilityReportsDto Report(decimal fishFeeAmount, params StallComplianceDto[] stalls) =>
        Report(fishFeeAmount, 0m, 0m, 0m, 0m, stalls);

    private static FacilityReportsDto Report(
        decimal fishFeeAmount, decimal meatKilos, decimal meatAmount, decimal fishFrozen, decimal fishUnfrozenKilos,
        params StallComplianceDto[] stalls) => new(
        0m, 0m, 0m, 0m, stalls.Length, stalls.Length, 0, 0m,
        Array.Empty<RevenueTrendDto>(), null!, Array.Empty<SectionBreakdownDto>(), Array.Empty<TopStallDto>(),
        null!, null,
        new FeeTypeBreakdownDto(0m, fishFeeAmount, null,
            WeightMeasureAmount: meatAmount, MeatKilos: meatKilos, MeatWeightMeasureAmount: meatAmount,
            FishWeightMeasureFrozenAmount: fishFrozen, FishKilosWithoutFrozenRate: fishUnfrozenKilos),
        Array.Empty<FishKiloTrendDto>(), stalls);
}
