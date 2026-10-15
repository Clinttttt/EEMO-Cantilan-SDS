using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmNativePolishTests : TestContext
{
    [Fact]
    public void Long_NPM_paid_day_receipt_is_summarized_but_all_days_remain_available_on_demand()
    {
        var days = Enumerable.Range(1, 29)
            .Select(day => new MobileCollectorRecordDto("—", "Ana Reyes", FacilityCode.NPM, "New Public Market",
                "1", "Daily Fee", 30m, 30m, false, new DateTime(2026, 10, 8, 0, 9, 0),
                FeeDate: new DateOnly(2026, 8, day))).ToArray();
        var cut = RenderComponent<DailyReceiptBreakdown>(p => p.Add(x => x.Items, days));

        Assert.Contains("29 covered days", cut.Find(".daily-breakdown-count").TextContent);
        Assert.Contains("Aug 1–29, 2026", cut.Find(".daily-breakdown-summary").TextContent);
        Assert.Contains("View daily breakdown (29 entries)", cut.Find("summary").TextContent);
        Assert.Equal(29, cut.FindAll("details .daily-breakdown-item").Count);
        Assert.Equal("₱30.00", cut.Find(".daily-breakdown-item strong").TextContent);
    }

    [Fact]
    public void Nonconsecutive_dates_are_not_presented_as_continuous_coverage()
    {
        var dates = new[] { 1, 2, 4, 6 };
        var days = dates.Select(day => new MobileCollectorRecordDto("—", "Ana Reyes", FacilityCode.NPM,
            "New Public Market", "1", "Daily Fee", 30m, 30m, false, new DateTime(2026, 10, 8),
            FeeDate: new DateOnly(2026, 8, day))).ToArray();
        var cut = RenderComponent<DailyReceiptBreakdown>(p => p.Add(x => x.Items, days));
        Assert.Contains("not continuous", cut.Find(".daily-breakdown-summary").TextContent);
        Assert.Equal(4, cut.FindAll(".daily-breakdown-item").Count);
    }

    [Fact]
    public void Missing_request_endpoint_shows_actionable_unavailable_state_not_an_empty_history()
    {
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmRequestsAsync(1, 25))
            .ReturnsAsync(Result<NpmRegistrationRequestPage>.NotFound());
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSessionService());

        var cut = RenderComponent<NpmRequests>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Request history unavailable", cut.Markup);
            Assert.Contains("updated server and its approved migration", cut.Markup);
            Assert.DoesNotContain("No requests yet", cut.Markup);
            Assert.Single(cut.FindAll(".npm-history-actions button"));
            Assert.Empty(cut.FindAll(".npm-history-list"));
        });
    }
}
