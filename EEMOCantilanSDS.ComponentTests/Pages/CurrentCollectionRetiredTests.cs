using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Client.Components.Pages.Collections;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Current Collection is retired: its old route only hands the visitor to Collection Activity.</summary>
public sealed class CurrentCollectionRetiredTests : TestContext
{
    [Fact]
    public void TheOldRoute_RedirectsToCollectionActivity()
    {
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IMunicipalitiesApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.ISetupApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IStallsApiClient>());
        Services.AddSingleton(Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Interface.ApiClients.IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        Assert.Contains(typeof(CurrentCollection).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>(), r => r.Template == "/collections/current");

        RenderComponent<CurrentCollection>();

        Assert.EndsWith("/collections/activity", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
