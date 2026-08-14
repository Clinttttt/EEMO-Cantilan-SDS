using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using Home = EEMOCantilanSDS.Client.Components.Pages.Home;

/// <summary>
/// The bare domain — console.stalltrack.site with no path.
///
/// <para>
/// There was no route for "/" at all, and the router carries no NotFound, so the address the office types answered 404. The root
/// now forwards: to the menu when the caller is signed in, to sign-in when they are not.
/// </para>
///
/// <para>
/// Both directions are asserted. A redirect that always went to one of them would pass a single-sided test while either
/// locking out a signed-in officer or handing the menu to a stranger.
/// </para>
/// </summary>
public class HomeRedirectTests : TestContext
{
    private FakeNavigationManager Navigate(bool signedIn)
    {
        var auth = this.AddTestAuthorization();
        if (signedIn) auth.SetAuthorized("cly.sullano");
        else auth.SetNotAuthorized();

        RegisterSharedImports();

        RenderComponent<Home>();
        return Services.GetRequiredService<FakeNavigationManager>();
    }

    /// <summary>The shared _Imports.razor injects these into every component; stubbed so the page resolves.</summary>
    private void RegisterSharedImports()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    [Fact]
    public void ASignedInOfficerLandsOnTheMenu()
    {
        var navigation = Navigate(signedIn: true);

        Assert.EndsWith("/menu", navigation.Uri);
    }

    [Fact]
    public void AVisitorWhoIsNotSignedInLandsOnSignIn()
    {
        var navigation = Navigate(signedIn: false);

        Assert.EndsWith("/login", navigation.Uri);
    }

    [Fact]
    public void TheRootDoesNotStayInHistory()
    {
        // The root is a junction, not a destination: left in history, Back returns here and is forwarded again, which reads as
        // a broken button.
        var navigation = Navigate(signedIn: true);

        var last = navigation.History.Last();
        Assert.True(last.Options.ReplaceHistoryEntry,
            "the redirect must replace the root in history rather than pushing a new entry");
    }

    [Fact]
    public void TheRootRendersNothingOfItsOwn()
    {
        // No landing page: one more screen between an officer and their work, and it would have to be designed twice over.
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("cly.sullano");
        RegisterSharedImports();

        var cut = RenderComponent<Home>();

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }
}
