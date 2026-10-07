using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class TodaysWorkMenuTests : TestContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Terminal_is_one_ready_tile_in_its_official_group_and_opens_the_native_route(bool explicitFamily)
    {
        var api = new Mock<IMobileApiClient>();
        var session = new MobileSessionService();
        session.Menu = session.Menu with { Facilities = [new(FacilityCode.TRM, "Transport Terminal", "", true, true, BillingArchetype.PerTrip)] };
        api.Setup(x => x.GetOperationCapabilitiesAsync()).ReturnsAsync(Result<CollectorOperationCapabilitiesDto>.Success(
            new(session.Menu.CollectorId, session.Menu.Today, [
                new(CollectorOperationCodes.Terminal, "Income From Terminal", true, CollectorOperationCapabilityStatus.Ready, true, [],
                    Family: explicitFamily ? CollectionFamily.Terminal : null),
                new(CollectorOperationCodes.Transportation, "Transportation / Parking", true, CollectorOperationCapabilityStatus.Ready, true, [], Family: CollectionFamily.Market)])));
        api.Setup(x => x.GetCollectionSessionDiscoveryAsync(null)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, session.Menu.Today, [])));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(session);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));

        var view = RenderComponent<Menu>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".menu-row-name"), x => x.TextContent == "Income From Terminal"));
        Assert.DoesNotContain("Transport Terminal", view.Markup);
        var heading = view.FindAll("h3").Single(x => x.TextContent == "Income from Terminal");
        Assert.Contains("Income From Terminal", heading.NextElementSibling!.TextContent);
        Assert.DoesNotContain("Transportation / Parking", heading.NextElementSibling.TextContent);
        Assert.DoesNotContain(view.FindAll("h3"), x => x.TextContent == "Other operations");
        view.FindAll("button.menu-row").Single(x => x.TextContent.Contains("Income From Terminal")).Click();
        Assert.EndsWith("/income-terminal", Services.GetRequiredService<NavigationManager>().Uri);
        view.FindAll("button.menu-row").Single(x => x.TextContent.Contains("Transportation / Parking")).Click();
        Assert.EndsWith("/operation/TRANSPORTATION", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
