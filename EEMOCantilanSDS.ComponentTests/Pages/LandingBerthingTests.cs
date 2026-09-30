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
/// Landing / Berthing once ran on sample rows ("Fishing Vessel 12", "Juan Dela Cruz · Collector B"), a "Record
/// Collection" dialog that saved nothing, and a report with hard-coded office-sheet figures and a CSV of the samples.
/// It now shows the real policy, states that collections are not yet recorded, and reports "—".
/// </summary>
public sealed class LandingBerthingTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly string[] RetiredContent =
        ["Fishing Vessel 12", "Juan Dela Cruz", "Collector A", "Record Collection", "sample", "300,000", "204,610", "12,960"];

    public LandingBerthingTests()
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
    public void Report_PrintsUnavailableFigures_WithoutSampleExport()
    {
        var cut = RenderComponent<LandingBerthingReport>();

        Assert.Empty(cut.FindAll("main"));
        var sheet = cut.Find("article.print-report-sheet");
        Assert.Contains("Republic of the Philippines", sheet.TextContent);
        var official = sheet.QuerySelector("section[aria-labelledby='lbr-official'] tbody tr")!;
        Assert.All(official.QuerySelectorAll("td"), td => Assert.Equal("—", td.TextContent.Trim()));
        Assert.DoesNotContain("₱", sheet.TextContent);
        Assert.DoesNotContain(cut.FindAll("a"), a => a.HasAttribute("download"));
        AssertNoRetiredContent(cut);
    }

    private static RevenueClassificationDto Classification(string code, string name) => new(
        Guid.NewGuid(), code, true, true,
        new RevenueClassificationPolicyDto(Guid.NewGuid(), new DateOnly(2026, 1, 1), name, null,
            RevenueInstrumentType.CashTicket, DateTime.UtcNow, "head"));

    private static void AssertNoRetiredContent<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        foreach (var text in RetiredContent)
            Assert.DoesNotContain(text, cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
