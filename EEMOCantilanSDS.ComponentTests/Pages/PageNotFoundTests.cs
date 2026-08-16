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
        // The whole point: a 404 the office cannot leave is the fault being fixed.
        var link = Render().Find("a");

        Assert.Contains("Go to", link.TextContent);
        Assert.False(string.IsNullOrWhiteSpace(link.GetAttribute("href")));
    }

    [Fact]
    public void TheWayBackIsALINKAndNotAClickHandler()
    {
        // Load-bearing, not stylistic. The server re-executes to this page for a mistyped address and that render is STATIC —
        // there is no circuit — so an @onclick would be wired to nothing and the only way off the page would silently do
        // nothing. An href works with no interactivity at all, and is the right element for navigating besides.
        var page = Render();

        Assert.Empty(page.FindAll("button"));
        Assert.Single(page.FindAll("a[href]"));
    }

    [Fact]
    public void AVisitorWhoIsNotSignedInIsSentToSignIn()
    {
        // No token, so nothing is known about them: they cannot be dropped into a menu they may have no right to, and the
        // component must not guess a role. Also the case that fails softly if TokenService ever returns null.
        var link = Render().Find("a");

        Assert.Equal("/login", link.GetAttribute("href"));
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
    public void ItRendersWithNoAppChrome()
    {
        // The page declares MinimalLayout, so its surroundings do not depend on the address in the bar. That matters here more
        // than anywhere: on a not-found render the path IS the mistyped address, so MainLayout's path-based sidebar rule would
        // have drawn the whole application shell around it — a menu offered to a visitor who may not be signed in, and a choice
        // of whose chrome to show for an address belonging to no area. Caught while wiring the router, not by inspection.
        Assert.Equal(
            typeof(EEMOCantilanSDS.Client.Components.Layout.MinimalLayout),
            Attribute.GetCustomAttribute(
                typeof(EEMOCantilanSDS.Client.Components.Pages.NotFound),
                typeof(Microsoft.AspNetCore.Components.LayoutAttribute)) is Microsoft.AspNetCore.Components.LayoutAttribute a
                ? a.LayoutType
                : null);
    }

    [Fact]
    public void TheFallbackDocumentIsAWHOLEPageAndCarriesTheStylesheet()
    {
        // The fallback endpoint renders a COMPONENT, and nothing supplies the document around it. The first working version
        // answered a mistyped address with 219 bytes of bare markup, which a browser shows as unstyled black text on white -
        // caught by running the app locally rather than by any test, which is why this one exists.
        //
        // app.css is the only stylesheet asserted because it is the only one this page needs: .empty-state, .btn-primary and the
        // colour variables all live in it.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(new TokenService());

        var markup = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.NotFoundDocument>().Markup;

        Assert.Contains("<!DOCTYPE html>", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app.css", markup);
        Assert.Contains("Page not found", markup);          // the shared component really is inside it
        Assert.Contains("href=\"/login\"", markup);

        // No Blazor script: the page has nothing that changes and its only control is a link, so it must not depend on a circuit
        // connecting - which is also why the way back is an anchor.
        Assert.DoesNotContain("blazor.web.js", markup);
    }

    [Fact]
    public void TheROUTERIsWiredToItForAnUnmatchedAddress()
    {
        // Asserts the WIRING, not a render, and deliberately so. The tests above all pass whether or not the router knows this
        // page exists — they render it directly — and the original defect was precisely that nothing pointed at it.
        //
        // A render cannot be asserted honestly here. The framework states that <NotFound> markup "isn't effective" in a Blazor
        // Web App, and production proved it: that markup shipped and a mistyped address still answered 404 with an empty body,
        // because endpoint routing rejects an unmatched path before any component renders. bUnit does honour the markup, so a
        // render-based test PASSED while the live site stayed blank — a test that lied. The .NET 10 mechanism is
        // Router.NotFoundPage, which needs real server-side routing, so what is checked here is that it is set and points at the
        // right page. The live 404 response is what confirms the behaviour.
        this.AddTestAuthorization();

        var setup = new Mock<ISetupApiClient>();
        setup.Setup(s => s.GetSetupStatusAsync()).ReturnsAsync(Result<SetupStatusDto>.Failure("not needed"));
        Services.AddSingleton(setup.Object);
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(new TokenService());
        // MainLayout renders the loading bar for every page, standalone or not, so the router test needs it even though this
        // page shows nothing that loads.
        Services.AddSingleton<UiLoadingService>();

        // Pointed at an address no page claims, so the router does not render a real page and drag its whole layout chain
        // (and every service that chain injects) into a test about one parameter.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/an-address-no-page-claims");

        var router = RenderComponent<EEMOCantilanSDS.Client.Components.Routes>()
            .FindComponent<Microsoft.AspNetCore.Components.Routing.Router>();

        Assert.Equal(
            typeof(EEMOCantilanSDS.Client.Components.Pages.NotFound),
            router.Instance.NotFoundPage);
    }
}
