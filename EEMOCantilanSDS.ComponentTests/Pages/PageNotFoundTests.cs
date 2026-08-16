using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Securities;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// What the office sees when it opens an address that does not exist.
///
/// <para>
/// Until this component existed the router had a <c>&lt;Found&gt;</c> branch and nothing else, so an unmatched address rendered an
/// EMPTY document. Verified against production before it was fixed: a mistyped URL answered 404 with a body of zero bytes — a
/// blank white page stating nothing and offering no way back. The status code was already right; only the page was missing.
/// </para>
///
/// <para>
/// Held here rather than left to inspection because a blank page is exactly the kind of fault nobody reports: it looks like the
/// network, or the browser, or a bad link, and the office works around it by retyping the address.
/// </para>
/// </summary>
public class PageNotFoundTests : TestContext
{
    private IRenderedComponent<PageNotFound> Render()
    {
        // The shared _Imports injects these into EVERY component, so they must be present even though this page uses none of
        // them. BrandingState is registered but never consulted here, which is the point of the last test: the page HAS the
        // office's identity available and deliberately does not put it on screen.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(new TokenService());

        return RenderComponent<PageNotFound>();
    }

    [Fact]
    public void ItSaysWhatHappened()
    {
        var page = Render();

        // The heading is an h1 rather than a styled div, both for a screen reader and because the router's FocusOnNavigate
        // selects "h1" — a page whose only title is a div gives it nothing to move focus to.
        Assert.Equal("Page not found", page.Find("h1").TextContent.Trim());
        Assert.Contains("does not exist", page.Find("p").TextContent);
    }

    [Fact]
    public void ItOffersAWayBack()
    {
        // The whole point. A 404 the office cannot leave is the fault being fixed, so the action is asserted to exist and to
        // be actionable rather than decorative.
        var button = Render().Find("button");

        Assert.False(button.HasAttribute("disabled"));
        Assert.Contains("Go to", button.TextContent);
    }

    [Fact]
    public void AVisitorWhoIsNotSignedInIsSentToSignIn()
    {
        // No token, so nothing is known about them: they cannot be dropped into a menu they may have no right to, and the
        // component must not guess a role. Also the case that fails softly if TokenService ever returns null.
        var page = Render();
        var navigation = Services.GetRequiredService<NavigationManager>();

        page.Find("button").Click();

        Assert.EndsWith("/login", navigation.Uri);
    }

    [Fact]
    public void NoTenantIsNamedAnywhereOnIt()
    {
        // This page renders for an address the platform does not recognise, on any municipality's host, to a visitor who may
        // not be signed in. Naming an office or a municipality here would put one LGU's identity on another's screen, so the
        // copy is deliberately generic — and pinned, because "helpful" copy is exactly what gets added later.
        var markup = Render().Markup;

        foreach (var tenantSpecific in new[] { "Cantilan", "EEMO", "Surigao", "Fish Area" })
            Assert.DoesNotContain(tenantSpecific, markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheROUTERActuallyShowsItForAnUnmatchedAddress()
    {
        // The assertion that covers the FIX rather than the component. Every test above passes with the router's <NotFound>
        // branch deleted — verified by deleting it — because they render the page directly. The defect was never the page; it
        // was that nothing asked for one. So this renders the real Routes component, navigates to an address no page claims,
        // and requires the message to appear.
        this.AddTestAuthorization();

        var setup = new Mock<ISetupApiClient>();
        setup.Setup(s => s.GetSetupStatusAsync()).ReturnsAsync(Result<SetupStatusDto>.Failure("not needed"));
        Services.AddSingleton(setup.Object);
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(new TokenService());

        Services.GetRequiredService<NavigationManager>().NavigateTo("/an-address-no-page-claims");

        var app = RenderComponent<EEMOCantilanSDS.Client.Components.Routes>();

        Assert.Contains("Page not found", app.Markup);
    }
}
