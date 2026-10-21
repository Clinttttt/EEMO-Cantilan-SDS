using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// A Public Market its LGU declared as standard monthly rental is managed like TCC/NCC, not on the daily calendar. The Head Portal reads that
/// from the facility's stored archetype (never from a rate or a name); a daily market such as Cantilan's keeps its daily page untouched.
/// </summary>
public sealed class MonthlyPublicMarketUiTests : TestContext
{
    private FacilityState Catalog(BillingArchetype? marketArchetype)
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
            {
                new(FacilityCode.NPM, "Madrid Public Market", "MPM", 0, Archetype: marketArchetype),
                new(FacilityCode.TCC, "Madrid Commercial Center", "MCC", 0, Archetype: BillingArchetype.MonthlyRental)
            }));
        return new FacilityState(api.Object);
    }

    [Theory]
    [InlineData(BillingArchetype.MonthlyRental, true)]
    [InlineData(BillingArchetype.DailyStall, false)]
    [InlineData(null, false)]                                    // an older API that states nothing: the market is daily
    public async Task Only_a_declared_monthly_rental_market_is_managed_as_one(BillingArchetype? declared, bool expected)
    {
        var catalog = Catalog(declared);
        Assert.False(catalog.IsMonthlyRentMarket);                // before the catalog loads, a market is never assumed monthly
        await catalog.EnsureLoadedAsync();
        Assert.Equal(expected, catalog.IsMonthlyRentMarket);
    }

    [Fact]
    public async Task A_commercial_center_alone_never_makes_the_market_monthly()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
            {
                new(FacilityCode.TCC, "Commercial", "TCC", 0, Archetype: BillingArchetype.MonthlyRental)
            }));
        var catalog = new FacilityState(api.Object);
        await catalog.EnsureLoadedAsync();
        Assert.False(catalog.IsMonthlyRentMarket);
    }

    [Fact]
    public void A_daily_market_that_reaches_the_monthly_workspace_is_sent_back_to_its_calendar()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Catalog(BillingArchetype.DailyStall));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();

        RenderComponent<NpmMonthlyRent>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/npm", navigation.Uri);
    }
}
