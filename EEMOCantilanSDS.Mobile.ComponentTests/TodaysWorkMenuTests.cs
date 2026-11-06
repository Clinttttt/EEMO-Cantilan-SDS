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
    [Fact]
    public void ElectricityMenuRowUsesTheFullUtilityNameAndKeepsItsRoute()
    {
        var api = new Mock<IMobileApiClient>();
        var session = new MobileSessionService();
        api.Setup(x => x.GetOperationCapabilitiesAsync()).ReturnsAsync(Result<CollectorOperationCapabilitiesDto>.Success(
            new(session.Menu.CollectorId, session.Menu.Today, [])));
        api.Setup(x => x.GetCollectionSessionDiscoveryAsync(null)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(
            new(null, session.Menu.Today, [new(CollectionSessionItemKind.Electricity, "ECF", "Electricity", true, true, null, null, false, [], StandaloneAvailable: true)])));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(session);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new OfflineReadinessCoordinator(api.Object, Mock.Of<IConnectivityMonitor>()));
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));

        var cut = RenderComponent<Menu>();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".menu-row-name"), x => x.TextContent == "Electricity Consumption Fee"));
        cut.FindAll("button.menu-row").Single(x => x.TextContent.Contains("Electricity Consumption Fee")).Click();
        Assert.EndsWith("/ecf", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void LoadingSkeletonMatchesTheSummaryAndRows_WithoutOfferingCollectionActions()
    {
        var api = new Mock<IMobileApiClient>();
        var session = new MobileSessionService();
        var pending = new TaskCompletionSource<Result<CollectorOperationCapabilitiesDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Setup(x => x.GetOperationCapabilitiesAsync()).Returns(pending.Task);
        api.Setup(x => x.GetCollectionSessionDiscoveryAsync(null)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, session.Menu.Today, [])));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(session);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new OfflineReadinessCoordinator(api.Object, Mock.Of<IConnectivityMonitor>()));
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));

        var view = RenderComponent<Menu>();
        Assert.Equal("status", view.Find(".menu-skeleton").GetAttribute("role"));
        Assert.Equal("true", view.Find(".menu-skeleton").GetAttribute("aria-busy"));
        Assert.Equal(3, view.FindAll(".menu-skeleton .menu-summary-item").Count);
        Assert.Equal(3, view.FindAll(".menu-skeleton .menu-row").Count);
        Assert.Empty(view.FindAll(".menu-skeleton button, .menu-skeleton a"));

        pending.SetResult(Result<CollectorOperationCapabilitiesDto>.Success(new(session.Menu.CollectorId, session.Menu.Today, [])));
        view.WaitForAssertion(() =>
        {
            Assert.Empty(view.FindAll(".menu-skeleton"));
            Assert.Equal("Today's work", view.Find("h1").TextContent);
            Assert.DoesNotContain("Offline lists partially fetched", view.Markup);
            Assert.DoesNotContain("Preparing offline lists", view.Markup);
        });
    }

    [Theory]
    [InlineData(CollectorOperationCodes.KanmanggaySpaceRental)]
    [InlineData(CollectorOperationCodes.FiestaArawLotRental)]
    public void Ready_space_rentals_open_source_filtered_new_collection(string code)
    {
        var api = new Mock<IMobileApiClient>();
        var session = new MobileSessionService();
        api.Setup(x => x.GetOperationCapabilitiesAsync()).ReturnsAsync(Result<CollectorOperationCapabilitiesDto>.Success(
            new(session.Menu.CollectorId, session.Menu.Today, [
                new(code, code == CollectorOperationCodes.KanmanggaySpaceRental ? "Kanmanggay" : "Fiesta / Araw",
                    true, CollectorOperationCapabilityStatus.Ready, true, [], Family: CollectionFamily.Space)])));
        api.Setup(x => x.GetCollectionSessionDiscoveryAsync(null)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(
            new(null, session.Menu.Today, [])));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(session);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new OfflineReadinessCoordinator(api.Object, Mock.Of<IConnectivityMonitor>()));
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));

        var view = RenderComponent<Menu>();
        var name = code == CollectorOperationCodes.KanmanggaySpaceRental ? "Kanmanggay" : "Fiesta / Araw";
        // The separate Arrears & Delinquency shortcut is not an operation and must remain visible.
        view.WaitForAssertion(() => Assert.Single(view.FindAll("button.menu-row"),
            button => button.TextContent.Contains(name, StringComparison.Ordinal)));
        Assert.Contains(view.FindAll("button.menu-row"), button => button.TextContent.Contains("Arrears & Delinquency"));
        Assert.DoesNotContain("Collected from the stall sheet", view.Markup);
        view.FindAll("button.menu-row").Single(button => button.TextContent.Contains(name, StringComparison.Ordinal)).Click();
        Assert.EndsWith("/collections/new?operation=" + code, Services.GetRequiredService<NavigationManager>().Uri);
    }

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
        Services.AddSingleton(new OfflineReadinessCoordinator(api.Object, Mock.Of<IConnectivityMonitor>()));
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
