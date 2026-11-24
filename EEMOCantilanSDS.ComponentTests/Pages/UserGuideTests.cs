using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The User Guide (panel, User Adoption) walks the office through StallTrack in the order the work happens, linking to the real pages.</summary>
public class UserGuideTests : TestContext
{
    [Fact]
    public void TheGuide_ListsEachPartOfTheWork_AndEveryLinkIsARealPage_WithoutATenantsNameOrRate()
    {
        var facilities = Mock.Of<IFacilitiesApiClient>();
        Services.AddSingleton(facilities);
        Services.AddSingleton(new FacilityState(facilities));
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton(Mock.Of<IReportsApiClient>());
        Services.AddSingleton<BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Admin").SetRoles("Admin");

        var cut = RenderComponent<UserGuide>();

        string[] sections = ["Getting started", "Billing rules", "Collecting at the office", "Collecting in the field", "Remittance", "Following up accounts", "Reports", "Payor portal"];
        Assert.Equal(sections, cut.FindAll(".ug-section h2").Select(h => h.TextContent.Trim()).ToArray());
        Assert.Equal(sections, cut.FindAll(".ug-toc a").Select(a => a.TextContent.Trim()).ToArray());
        Assert.All(cut.FindAll(".ug-toc a"), a => Assert.StartsWith("/settings/user-guide#", a.GetAttribute("href")));

        string[] pages = ["/settings", "/settings/billing-policy", "/operations", "/operations/fines", "/online-payments", "/collectors", "/collections/activity",
            "/remittances", "/monitoring/follow-up", "/vendors", "/reports/billing-register", "/reports/month-end", "/reports", "/audit-trail"];
        Assert.Equal(pages, cut.FindAll(".ug-steps a").Select(a => a.GetAttribute("href")).ToArray());
        // The page's name inside its sentence is the link; it is never repeated as a separate label after the sentence.
        Assert.All(cut.FindAll(".ug-steps a"), a => Assert.Equal(2, a.ParentElement!.TextContent.Split(a.TextContent).Length));

        foreach (var tenantDetail in new[] { "Cantilan", "EEMO", "₱", "25%", "36 months" })
            Assert.DoesNotContain(tenantDetail, cut.Find(".ug-body").TextContent);
    }
}
