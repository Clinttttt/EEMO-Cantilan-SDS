using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Market Fees once showed sample sections and daily totals and let staff "record" totals that were never saved; its
/// report carried hard-coded office-sheet figures. The workspace now shows the real revenue policy and states that
/// collections are not yet recorded; the report prints "—" instead of typed-in numbers.
/// </summary>
public sealed class MarketFeesTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly string[] RetiredContent =
        ["Comfort Room", "General Market Area", "Collector A", "Record Daily Totals", "Add Section", "1,300,000", "69,990", "2,150"];

    public MarketFeesTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        // Operation reports read the official Monthly Income and the governed-activity register; an unanswered read
        // shows "—", which is what these tests assert.
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton(Mock.Of<IGovernedServicesApiClient>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Report_PrintsUnavailableFigures_NotTypedInOnes()
    {
        var cut = RenderComponent<MarketFeesReport>();

        Assert.Empty(cut.FindAll("main"));
        var sheet = cut.Find("article.print-report-sheet");
        Assert.Contains("Republic of the Philippines", sheet.TextContent);
        var official = sheet.QuerySelector("section[aria-labelledby='mfr-official'] tbody tr")!;
        Assert.All(official.QuerySelectorAll("td"), td => Assert.Equal("—", td.TextContent.Trim()));
        Assert.DoesNotContain("₱", sheet.TextContent);
        AssertNoRetiredContent(cut);
    }

    private static void AssertNoRetiredContent<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        foreach (var text in RetiredContent)
            Assert.DoesNotContain(text, cut.Markup);
    }
}
