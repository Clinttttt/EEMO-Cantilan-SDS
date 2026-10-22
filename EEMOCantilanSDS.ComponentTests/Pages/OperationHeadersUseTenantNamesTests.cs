using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Operation workspace headers print the municipality's own name for the operation when a page gives them its stable code, and the
/// page's established title in every other case - a page rendered without the service, an office that never adopted configuration,
/// or a header given no code.
/// </summary>
public sealed class OperationHeadersUseTenantNamesTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public OperationHeadersUseTenantNamesTests()
    {
        // The shared _Imports injects these into every component; stubbed so the components resolve.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    private void Names(bool adopted, string? label)
    {
        var labels = label is null ? null : new Dictionary<string, string> { ["KANMANGGAY_SPACE_RENTAL"] = label };
        var api = OperationsTenantConfigurationTests.SettingsApi(adopted, ["KANMANGGAY_SPACE_RENTAL"], labels);
        Services.AddSingleton(new OperationNameService(api.Object));
    }

    [Fact]
    public void OperationTopbar_DefaultsToOperations_ForDirectPageAccess()
    {
        this.AddTestAuthorization().SetAuthorized("head");
        var cut = RenderComponent<OperationTopbar>(p => p.Add(c => c.Title, "Kanmanggay").Add(c => c.Crumb, "Space rental"));
        Assert.Equal("/operations", cut.Find(".tcc-back-btn").GetAttribute("href"));
        Assert.Equal("Back to Operations", cut.Find(".tcc-back-btn").GetAttribute("aria-label"));
        Assert.Contains(cut.FindAll(".breadcrumb-link"), a => a.GetAttribute("href") == "/operations");
    }

    [Theory]
    [InlineData(typeof(KanmanggayImport), "/operations/kanmanggay")]
    [InlineData(typeof(VegetableFruitImport), "/operations/vegetable-fruit")]
    [InlineData(typeof(FiestaArawImport), "/operations/fiesta-araw")]
    public void ImportPage_ReturnsToItsOperation_WithoutBrowserHistory(Type page, string parent)
    {
        this.AddTestAuthorization().SetAuthorized("head");
        ComponentFactories.AddStub<SpaceHolderImport>();
        var cut = RenderComponent<Microsoft.AspNetCore.Components.DynamicComponent>(p => p.Add(c => c.Type, page));
        Assert.Equal(parent, cut.Find(".tcc-back-btn").GetAttribute("href"));
        Assert.Contains(cut.FindAll(".breadcrumb-link"), a => a.GetAttribute("href") == parent);
    }

    [Theory]
    [InlineData("/operations/fish-meat-vendor-fees?taxYear=2025", "Fish / Meat Vendors", true)]
    [InlineData("/monitoring/review", "Collection & Registration Review", false)]
    [InlineData("/monitoring/follow-up", "Monitoring", false)]
    public void OperationTopbar_UsesItsExplicitParentAndContext(string href, string title, bool operations)
    {
        this.AddTestAuthorization().SetAuthorized("head");
        var cut = RenderComponent<OperationTopbar>(p => p.Add(c => c.Title, "Review")
            .Add(c => c.Crumb, "Details").Add(c => c.ParentHref, href).Add(c => c.ParentTitle, title));
        Assert.Equal(href, cut.Find(".tcc-back-btn").GetAttribute("href"));
        Assert.Equal($"Back to {title}", cut.Find(".tcc-back-btn").GetAttribute("aria-label"));
        Assert.Contains(cut.FindAll(".breadcrumb-link"), a => a.GetAttribute("href") == href && a.TextContent == title);
        Assert.Equal(operations, cut.FindAll(".breadcrumb-link").Any(a => a.GetAttribute("href") == "/operations"));
    }

    [Fact]
    public void WorkspaceHeader_ShowsTheOfficesNameForTheOperation_InTheBreadcrumbAndTheHeading()
    {
        Names(adopted: true, label: "Municipal Plaza Rental");

        var cut = RenderComponent<WorkspaceHeader>(p => p
            .Add(c => c.OperationCode, "KANMANGGAY_SPACE_RENTAL").Add(c => c.Title, "Kanmanggay").Add(c => c.Parent, "Operations"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Municipal Plaza Rental", cut.Find("h1").TextContent.Trim());
            Assert.Contains("Municipal Plaza Rental", cut.Find("nav").TextContent);
            Assert.DoesNotContain("Kanmanggay", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void WorkspaceHeader_FallsBackToItsTitle_ForAnOfficeThatNeverAdopted_AndWithoutACode()
    {
        Names(adopted: false, label: null);

        var legacy = RenderComponent<WorkspaceHeader>(p => p
            .Add(c => c.OperationCode, "KANMANGGAY_SPACE_RENTAL").Add(c => c.Title, "Kanmanggay"));
        legacy.WaitForAssertion(() => Assert.Equal("Kanmanggay", legacy.Find("h1").TextContent.Trim()), Timeout);

        var noCode = RenderComponent<WorkspaceHeader>(p => p.Add(c => c.Title, "Daily collection setup"));
        Assert.Equal("Daily collection setup", noCode.Find("h1").TextContent.Trim());
    }

    [Fact]
    public void AHeaderRenderedWithoutTheService_ShowsItsOwnTitle()
    {
        // No OperationNameService registered at all (a preview, an older test host).
        var cut = RenderComponent<WorkspaceHeader>(p => p
            .Add(c => c.OperationCode, "KANMANGGAY_SPACE_RENTAL").Add(c => c.Title, "Kanmanggay"));
        Assert.Equal("Kanmanggay", cut.Find("h1").TextContent.Trim());
    }

    [Fact]
    public void OperationTopbar_AndV3Hero_FollowTheSameName()
    {
        Names(adopted: true, label: "Municipal Plaza Rental");

        this.AddTestAuthorization().SetAuthorized("head"); // the topbar carries the account chip; registered after every other service
        var bar = RenderComponent<OperationTopbar>(p => p
            .Add(c => c.OperationCode, "KANMANGGAY_SPACE_RENTAL").Add(c => c.Title, "Kanmanggay").Add(c => c.Crumb, "Space rental"));
        var hero = RenderComponent<V3Hero>(p => p
            .Add(c => c.OperationCode, "KANMANGGAY_SPACE_RENTAL").Add(c => c.Title, "Kanmanggay"));

        bar.WaitForAssertion(() => Assert.Equal("Municipal Plaza Rental", bar.Find(".topbar-title").TextContent.Trim()), Timeout);
        hero.WaitForAssertion(() => Assert.Equal("Municipal Plaza Rental", hero.Find("h1").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task ARenamedOperation_RedrawsAnOpenHeader_WithoutAReload()
    {
        var api = OperationsTenantConfigurationTests.SettingsApi(true, ["KANMANGGAY_SPACE_RENTAL"],
            new Dictionary<string, string> { ["KANMANGGAY_SPACE_RENTAL"] = "First Name" });
        var service = new OperationNameService(api.Object);
        Services.AddSingleton(service);
        var cut = RenderComponent<WorkspaceHeader>(p => p
            .Add(c => c.OperationCode, "KANMANGGAY_SPACE_RENTAL").Add(c => c.Title, "Kanmanggay"));
        cut.WaitForAssertion(() => Assert.Equal("First Name", cut.Find("h1").TextContent.Trim()), Timeout);

        api.Setup(c => c.GetAsync()).ReturnsAsync(OperationsTenantConfigurationTests.SettingsApi(true, ["KANMANGGAY_SPACE_RENTAL"],
            new Dictionary<string, string> { ["KANMANGGAY_SPACE_RENTAL"] = "Second Name" }).Object.GetAsync().Result);
        await service.InvalidateAsync();

        cut.WaitForAssertion(() => Assert.Equal("Second Name", cut.Find("h1").TextContent.Trim()), Timeout);
    }
}
