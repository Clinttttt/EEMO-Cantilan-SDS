using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Shared;
using Moq;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class MonitoringTabsNpmTests : TestContext
{
    public MonitoringTabsNpmTests()
    {
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
    }
    [Theory]
    [InlineData("follow-up")]
    [InlineData("contracts")]
    [InlineData("review")]
    public void Monitoring_tabs_link_the_three_primary_registers(string current)
    {
        var view = RenderComponent<MonitoringTabs>(p => p.Add(x => x.Current, current));
        Assert.Equal(3, view.FindAll("a.mt-tab").Count);
        Assert.Contains("/monitoring/review", view.Markup);
        Assert.Equal(1, view.FindAll("a[aria-current='page']").Count);
        var active = view.Find("a[aria-current='page']");
        Assert.Contains(current == "follow-up" ? "/monitoring/follow-up" : "/monitoring/" + current,
                        active.GetAttribute("href"));
    }
}
