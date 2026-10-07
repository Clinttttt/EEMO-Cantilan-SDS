using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Business Payors are retired: the old address only forwards to Spaces & Occupants and no page manages them.</summary>
public sealed class BusinessPayorsRetiredTests : TestContext
{
    [Fact]
    public void TheOldAddressForwardsToSpacesAndOccupants_AndNoLinkWorkflowRemains()
    {
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IMunicipalitiesApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.ISetupApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IStallsApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        Assert.Contains(typeof(BusinessPayorsRetired).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>(), r => r.Template == "/business-payors");

        RenderComponent<BusinessPayorsRetired>();

        Assert.EndsWith("/vendors", Services.GetRequiredService<NavigationManager>().Uri);
        var shared = typeof(EEMOCantilanSDS.Client.Components.Shared.AddNewDrawer).Assembly.GetTypes().Select(t => t.Name);
        Assert.DoesNotContain("LinkPayorDialog", shared);
        Assert.DoesNotContain("PayorPickerDrawer", shared);
    }
}
