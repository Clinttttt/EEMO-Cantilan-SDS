using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Fish / Meat Vendor Fee and independent Weight &amp; Measure remain separate operations.
/// Existing NPM weighing evidence is historical and frozen, while new registered-vendor weighing
/// has its own source-native read model. Neither view recalculates amounts from current stall rent.
/// </summary>
public sealed class FishMeatWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid FishStallId = Guid.NewGuid();
    private static readonly string[] ForbiddenActions =
        ["Add Vendor", "Add vendor", "Assign", "Collect", "Post", "Record", "Issue OR", "Enter kilos", "Save"];

    private readonly Mock<IFacilitiesApiClient> _facilities = new();
    private readonly Mock<IStallsApiClient> _stalls = new();
    private readonly Mock<IObligationsApiClient> _obligations = new();
    private readonly TestAuthorizationContext _authorization;

    public FishMeatWorkspaceTests()
    {
        _facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                new[] { new FacilitySidebarSummaryDto(FacilityCode.NPM, "Tenant Public Market", "TPM", 0) }));
        _stalls.Setup(x => x.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Success(new NpmRatesDto(30m, 1m)));
        // This page now reads an independent registered-vendor source in addition to the historical NPM source.
        // Supply both read models so existing historical financial evidence assertions remain meaningful.
        _facilities.Setup(x => x.GetWeighingConfigurationAsync()).ReturnsAsync(
            Result<WeighingConfigurationDto>.Success(new(PhilippineTime.Today, PhilippineTime.Today.AddDays(1), [], [])));
        _facilities.Setup(x => x.GetWeighingSourceNativeReportAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<WeighingCollectionReportDto>.Success(new(PhilippineTime.Today.Year, PhilippineTime.Today.Month, [])));

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(_facilities.Object);
        Services.AddSingleton(_stalls.Object);
        Services.AddSingleton(_obligations.Object);
        Services.AddSingleton(Mock.Of<IEcfCollectionsApiClient>());
        Services.AddSingleton(Mock.Of<IBusinessPayorsApiClient>());
        Services.AddSingleton<FacilityState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _authorization = this.AddTestAuthorization();
        _authorization.SetAuthorized("admin").SetRoles("Admin");
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
    public void SuperAdmin_can_update_independent_fish_meat_rates_in_the_existing_operation_page()
    {
        _authorization.SetRoles("SuperAdmin");
        var today = PhilippineTime.Today;
        var next = today.AddDays(1);
        var current = new[]
        {
            new WeighingTypeConfigurationDto(FishMeatVendorType.Fish, true, 1m, today, true),
            new WeighingTypeConfigurationDto(FishMeatVendorType.Meat, true, 66m, today, true),
        };
        var upcoming = new[]
        {
            new WeighingTypeConfigurationDto(FishMeatVendorType.Fish, true, 1m, today, true),
            new WeighingTypeConfigurationDto(FishMeatVendorType.Meat, true, 66m, today, true),
        };
        _facilities.Setup(x => x.GetWeighingConfigurationAsync())
            .ReturnsAsync(Result<WeighingConfigurationDto>.Success(
                new WeighingConfigurationDto(today, next, current, upcoming)));
        _facilities.Setup(x => x.SetWeighingConfigurationAsync(
            It.Is<SetWeighingConfigurationRequest>(r =>
                r.FishEnabled && r.FishRatePerKilo == 2.50m
                && !r.MeatEnabled && r.MeatRatePerKilo == null)))
            .ReturnsAsync(Result<bool>.Success(true));
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly,
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(fishFeeAmount: 0m)));

        var cut = RenderComponent<WeightAndMeasure>();
        // The rates live in their own drawer, opened from the page header.
        cut.WaitForAssertion(() => Assert.Contains("Weighing rates", cut.Markup), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Weighing rates").Click();
        cut.WaitForAssertion(() => Assert.Contains("Edit rates", cut.Find("[role='dialog']").TextContent), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Edit rates").Click();
        var rates = cut.FindAll("input[type=number]");
        Assert.Equal(2, rates.Count);
        rates[0].Change("2.50");
        var enabled = cut.FindAll("input[type=checkbox]");
        Assert.Equal(2, enabled.Count);
        enabled[1].Change(false);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save rates").Click();

        cut.WaitForAssertion(() =>
        {
            _facilities.Verify(x => x.SetWeighingConfigurationAsync(
                It.Is<SetWeighingConfigurationRequest>(r =>
                    r.FishEnabled && r.FishRatePerKilo == 2.50m
                    && !r.MeatEnabled && r.MeatRatePerKilo == null)), Times.Once);
            Assert.Contains("next collection business date", cut.Markup);
        }, Timeout);
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
            var summary = cut.Find(".wm-layout").TextContent;
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
    public void WeightAndMeasureReport_StatesTheFrozenServerFigures_AndTheFishRows_InTheStandardReportShell()
    {
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(fishFeeAmount: 37m, meatKilos: 4m, meatAmount: 120m, fishFrozen: 15m, fishUnfrozenKilos: 10m,
                Stall(FishStallId, "F-12", "Juan Dela Cruz", "Fish Area", fishKilos: 25m))));

        var cut = RenderComponent<WeightAndMeasureReport>();

        cut.OpenStatusReport();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            var sheet = cut.Find("article.print-report-sheet").TextContent;
            Assert.Contains("₱120.00", sheet);          // Meat weighing, frozen
            Assert.Contains("₱15.00", sheet);           // Fish weighing, frozen
            Assert.DoesNotContain("₱37.00", sheet);     // kilos x today's rate is never shown as collected
            Assert.Contains("Juan Dela Cruz", sheet);
            Assert.Contains("25.00 kg", sheet);
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

    [Theory]
    [InlineData(typeof(WeightAndMeasure), "Weighing month")]
    [InlineData(typeof(WeightAndMeasureReport), "Reporting month")]
    public async Task FailedPeriodChangeDoesNotRetainPreviousRegisteredVendorTotals(Type page, string monthLabel)
    {
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        _facilities.SetupSequence(x => x.GetWeighingSourceNativeReportAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<WeighingCollectionReportDto>.Success(new(2026, 10,
                [new(new(2026, 10, 1), "SRC-TEST", "Test vendor", FishMeatVendorType.Fish, 2m, 50m, 100m, "Recorded")])))
            .ThrowsAsync(new HttpRequestException("unavailable"));
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(fishFeeAmount: 0m)));
        var cut = Render(builder => { builder.OpenComponent(0, page); builder.CloseComponent(); });
        if (page == typeof(WeightAndMeasureReport)) cut.OpenStatusReport();
        cut.WaitForAssertion(() => Assert.Contains("Test vendor", cut.Markup), Timeout);
        if (page == typeof(WeightAndMeasure))
            Assert.Contains("₱100.00", cut.Find("dl[aria-label='Weighing position']").TextContent);

        if (page == typeof(WeightAndMeasureReport)) await cut.PreviousMonthAsync();

        else cut.Find($"input[aria-label='{monthLabel}']").Change("2026-09");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.DoesNotContain("Test vendor", cut.Markup);
            Assert.DoesNotContain("₱100.00", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void WeightAndMeasure_FailedHistoricalSourceDoesNotInventLegacyTotals_IndependentSourceStillDisplays()
    {
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Failure("offline"));

        var cut = RenderComponent<WeightAndMeasure>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Weighing records are unavailable.", cut.Markup);
            Assert.DoesNotContain("₱0.00", cut.Find("[aria-label='Historical NPM Fish weighing']").TextContent);
            Assert.Contains("₱0.00", cut.Find("dl[aria-label='Weighing position']").TextContent);
        }, Timeout);
    }

    [Theory]
    [InlineData(typeof(WeightAndMeasure), "Weighing month")]
    [InlineData(typeof(WeightAndMeasureReport), "Reporting month")]
    public async Task AnOlderPeriodResponseCannotReplaceTheLatestSelection(Type page, string monthLabel)
    {
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        static Result<WeighingCollectionReportDto> Source(string vendor, int month) =>
            Result<WeighingCollectionReportDto>.Success(new(2026, month,
                [new(new(2026, month, 1), "SRC-TEST", vendor, FishMeatVendorType.Fish, 1m, 30m, 30m, "Posted")]));
        var delayed = new TaskCompletionSource<Result<WeighingCollectionReportDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _facilities.SetupSequence(x => x.GetWeighingSourceNativeReportAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Source("Initial vendor", 10)).Returns(delayed.Task).ReturnsAsync(Source("Latest vendor", 8));
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(fishFeeAmount: 0m)));
        var cut = Render(builder => { builder.OpenComponent(0, page); builder.CloseComponent(); });
        if (page == typeof(WeightAndMeasureReport)) cut.OpenStatusReport();
        cut.WaitForAssertion(() => Assert.Contains("Initial vendor", cut.Markup), Timeout);
        var olderRequest = page == typeof(WeightAndMeasureReport) ? cut.PreviousMonthAsync() : cut.Find($"input[aria-label='{monthLabel}']").ChangeAsync(new ChangeEventArgs { Value = "2026-09" });
        if (page == typeof(WeightAndMeasureReport)) await cut.PreviousMonthAsync();
        else await cut.Find($"input[aria-label='{monthLabel}']").ChangeAsync(new ChangeEventArgs { Value = "2026-08" });
        cut.WaitForAssertion(() => Assert.Contains("Latest vendor", cut.Markup), Timeout);
        delayed.SetResult(Source("Older vendor", 9));
        await olderRequest;
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Latest vendor", cut.Markup);
            Assert.DoesNotContain("Older vendor", cut.Markup);
        }, Timeout);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RegisteredCollectionsStayInTheirExistingCardsEvenAfterConfigurationChanges(bool fishEnabled, bool meatEnabled)
    {
        _facilities.Setup(x => x.GetWeighingConfigurationAsync()).ReturnsAsync(Result<WeighingConfigurationDto>.Success(
            new(PhilippineTime.Today, PhilippineTime.Today.AddDays(1),
                [new(FishMeatVendorType.Fish, fishEnabled, 99m, PhilippineTime.Today, true),
                 new(FishMeatVendorType.Meat, meatEnabled, 99m, PhilippineTime.Today, true)], [])));
        SourceCollections();
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(0m)));
        var cut = RenderComponent<WeightAndMeasure>();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".wm-layout > section").Count);
            Assert.Empty(cut.FindAll("section[aria-label='Registered vendor weighing collections']"));
            var fish = cut.Find("section[aria-labelledby='wm-fish-title']").TextContent;
            var meat = cut.Find("section[aria-labelledby='wm-meat-title']").TextContent;
            Assert.Contains("Fish vendor", fish);
            Assert.DoesNotContain("Meat vendor", fish);
            Assert.Contains("SRC-FISH", fish);
            Assert.Contains("₱2.00/kg", fish);
            Assert.Contains("₱6.00", fish);
            Assert.Contains("Corrected", fish);
            Assert.Contains("Meat vendor", meat);
            Assert.DoesNotContain("Fish vendor", meat);
            Assert.Contains("SRC-MEAT", meat);
            Assert.Contains("₱4.00/kg", meat);
            Assert.Equal("/collections/activity?src=SRC-FISH", cut.Find("a[href*='src=SRC-FISH']").GetAttribute("href"));
        }, Timeout);

        cut.Find("input[type=search]").Input("SRC-MEAT");
        Assert.DoesNotContain("Fish vendor", cut.Markup);
        Assert.Contains("Meat vendor", cut.Markup);
        Assert.Contains("₱14.00", cut.Find("dl[aria-label='Weighing position']").TextContent);
    }

    [Theory]
    [InlineData(typeof(WeightAndMeasure))]
    [InlineData(typeof(WeightAndMeasureReport))]
    public void MunicipalityWithoutNpmStillShowsRegisteredWeighingWithoutRequestingNpm(Type page)
    {
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        _facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([]));
        SourceCollections();
        var cut = Render(builder => { builder.OpenComponent(0, page); builder.CloseComponent(); });
        if (page == typeof(WeightAndMeasureReport)) cut.OpenStatusReport();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Fish vendor", cut.Markup);
            Assert.Contains("Meat vendor", cut.Markup);
            Assert.DoesNotContain("Historical NPM", cut.Markup);
            Assert.Empty(cut.FindAll("a[href='/facility/npm']"));
        }, Timeout);
        _facilities.Verify(x => x.GetFacilityReportsAsync(FacilityCode.NPM, It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public void ReportAndCsvPreserveRegisteredAndFrozenHistoricalEvidenceInFishAndMeatSections()
    {
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        SourceCollections();
        _facilities.Setup(x => x.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(Report(37m, 4m, 120m, 15m, 10m)));
        var cut = RenderComponent<WeightAndMeasureReport>();
        cut.OpenStatusReport();
        cut.WaitForAssertion(() =>
        {
            var fish = cut.Find("section[aria-labelledby='wmr-fish']").TextContent;
            var meat = cut.Find("section[aria-labelledby='wmr-meat']").TextContent;
            Assert.Contains("SRC-FISH", fish);
            Assert.DoesNotContain("SRC-MEAT", fish);
            Assert.Contains("Corrected", fish);
            Assert.Contains("₱15.00", fish);
            Assert.Contains("10.00 kg", fish);
            Assert.Contains("SRC-MEAT", meat);
            Assert.Contains("₱120.00", meat);
            Assert.DoesNotContain("₱37.00", cut.Markup);
            var csv = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!);
            Assert.Contains("SRC-FISH", csv);
            Assert.Contains("Corrected", csv);
            Assert.Contains("Historical NPM frozen Fish amount\",\"15.00", csv);
            Assert.Contains("Historical NPM frozen Meat amount\",\"120.00", csv);
        }, Timeout);
    }

    private void SourceCollections() => _facilities.Setup(x => x.GetWeighingSourceNativeReportAsync(It.IsAny<int>(), It.IsAny<int>()))
        .ReturnsAsync(Result<WeighingCollectionReportDto>.Success(new(2026, 10,
            [new(new(2026, 10, 1), "SRC-FISH", "Fish vendor", FishMeatVendorType.Fish, 9m, 2m, 6m, "Corrected"),
             new(new(2026, 10, 2), "SRC-MEAT", "Meat vendor", FishMeatVendorType.Meat, 2m, 4m, 8m, "Posted")])));

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
