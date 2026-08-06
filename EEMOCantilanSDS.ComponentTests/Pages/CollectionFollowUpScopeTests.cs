using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The two follow-up screens are one module now, and the scope switch is the whole point of it: the office reads a
/// current-period queue or a whole-time account review, never a blend of the two. These tests hold the routing
/// contract — both former addresses still work, and each opens on the scope it names.
/// </summary>
public class CollectionFollowUpScopeTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Renders only the shell. The two hosted views need a dozen API clients between them, and what is under test
    /// here is the scope decision, so the children are stubbed out by an empty renderer.
    /// </summary>
    private IRenderedComponent<CollectionFollowUp> RenderAt(string relativeUrl)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        // _Imports.razor injects these into every component, so the shell needs them registered even though the
        // scope switch itself uses none of them.
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IMunicipalitiesApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IFacilitiesApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IPaymentsApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.ISetupApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.ISettingsApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IStallsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Head");

        ComponentFactories.AddStub<FollowUpQueue>();
        ComponentFactories.AddStub<PastFollowUpQueue>();

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(relativeUrl);

        return RenderComponent<CollectionFollowUp>();
    }

    [Fact]
    public void TheQueueAddressOpensOnTheCurrentPeriodScope()
    {
        var cut = RenderAt("/reports/follow-up");

        cut.WaitForAssertion(() =>
        {
            var active = cut.Find(".cfu-scope-active");
            Assert.Contains("Current Queue", active.TextContent);
            Assert.Single(cut.FindComponents<Stub<FollowUpQueue>>());
            Assert.Empty(cut.FindComponents<Stub<PastFollowUpQueue>>());
        }, RenderTimeout);
    }

    [Fact]
    public void TheHistoryAddressOpensOnTheWholeTimeScope()
    {
        // The office bookmarks and pastes this address; it must still land where it always did.
        var cut = RenderAt("/reports/follow-up/history");

        cut.WaitForAssertion(() =>
        {
            var active = cut.Find(".cfu-scope-active");
            Assert.Contains("Whole-time History", active.TextContent);
            Assert.Single(cut.FindComponents<Stub<PastFollowUpQueue>>());
            Assert.Empty(cut.FindComponents<Stub<FollowUpQueue>>());
        }, RenderTimeout);
    }

    [Fact]
    public void TheNewAddressOpensOnTheQueue_WhichIsTheMorningsWork()
    {
        var cut = RenderAt("/reports/collection-follow-up");

        cut.WaitForAssertion(
            () => Assert.Contains("Current Queue", cut.Find(".cfu-scope-active").TextContent),
            RenderTimeout);
    }

    [Fact]
    public void SwitchingScope_ChangesTheViewAndTheAddress()
    {
        var cut = RenderAt("/reports/follow-up");
        var nav = Services.GetRequiredService<NavigationManager>();

        // The second tab is the whole-time scope.
        cut.FindAll(".cfu-scope-tab")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Whole-time History", cut.Find(".cfu-scope-active").TextContent);
            Assert.Single(cut.FindComponents<Stub<PastFollowUpQueue>>());
            // Each scope stays linkable on its own, which is how the office shares them.
            Assert.EndsWith("/reports/follow-up/history", nav.Uri);
        }, RenderTimeout);

        cut.FindAll(".cfu-scope-tab")[0].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Current Queue", cut.Find(".cfu-scope-active").TextContent);
            Assert.EndsWith("/reports/follow-up", nav.Uri);
        }, RenderTimeout);
    }
}
