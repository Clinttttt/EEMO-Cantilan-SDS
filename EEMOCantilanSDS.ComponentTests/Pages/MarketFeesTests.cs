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
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Workspace_ForTheHead_ShowsTheRealPolicy_AndRecordsNothing()
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(x => x.GetClassificationsAsync(It.IsAny<DateOnly?>())).ReturnsAsync(
            Result<IReadOnlyList<RevenueClassificationDto>>.Success(new[]
            {
                new RevenueClassificationDto(Guid.NewGuid(), RevenueClassificationCodes.MarketFees, true, true,
                    new RevenueClassificationPolicyDto(Guid.NewGuid(), new DateOnly(2026, 1, 1), "Market Fees", null,
                        RevenueInstrumentType.CashTicket, DateTime.UtcNow, "head")),
            }));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            var policy = cut.Find("aside").TextContent;
            Assert.Contains("Cash Ticket", policy);
            Assert.Contains("Jan 1, 2026", policy);
            Assert.Contains("aren't recorded in StallTrack yet", cut.Markup);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/settings/revenue");

            Assert.Empty(cut.FindAll("form"));
            Assert.Empty(cut.FindAll("input"));
            Assert.Empty(cut.FindAll("[role='dialog']"));
            AssertNoRetiredContent(cut);
        }, Timeout);
    }

    [Fact]
    public void Workspace_ForAnAdmin_SaysPolicyIsHeadOnly_WithoutSetupLink()
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(x => x.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Failure("Forbidden", ResultStatus.Forbidden));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<MarketFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("available to the Head", cut.Find("aside").TextContent);
            Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href") == "/settings/revenue");
        }, Timeout);
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
