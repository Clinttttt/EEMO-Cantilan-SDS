using Bunit;
using Bunit.TestDoubles;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class VendorRegistrationDrawerTests : TestContext
{
    public VendorRegistrationDrawerTests()
    {
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ImportIsOptInForOrdinaryRegistrationOnly(bool optIn, bool renew, bool expected)
    {
        var cut = RenderComponent<VendorRegistrationDrawer>(p => p.Add(x => x.Show, true).Add(x => x.ShowImport, optIn)
            .Add(x => x.IsRenew, renew).Add(x => x.Model, new VendorRegistrationForm()));
        Assert.Equal(expected ? 1 : 0, cut.FindAll(".vendor-import-action button").Count);
    }

    [Fact]
    public void NpmSourceContextNeverOffersImport()
    {
        var cut = RenderComponent<VendorRegistrationDrawer>(p => p.Add(x => x.Show, true).Add(x => x.ShowImport, true)
            .Add(x => x.SourceContext, (RenderFragment)(b => b.AddContent(0, "Verified NPM association")))
            .Add(x => x.Model, new VendorRegistrationForm()));
        Assert.Empty(cut.FindAll(".vendor-import-action button"));
    }

    [Fact]
    public void AnAdminCannotSeeTheImportAction()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut = RenderComponent<VendorRegistrationDrawer>(p => p.Add(x => x.Show, true).Add(x => x.ShowImport, true)
            .Add(x => x.Model, new VendorRegistrationForm()));
        Assert.Empty(cut.FindAll(".vendor-import-action button"));
    }

    [Fact]
    public void ImportPreservesEditsUntilTheUserConfirmsNavigation()
    {
        var model = new VendorRegistrationForm { TaxYear = 2026 };
        var cut = RenderComponent<VendorRegistrationDrawer>(p => p.Add(x => x.Show, true).Add(x => x.ShowImport, true).Add(x => x.Model, model));
        cut.Find("input[id$='-name']").Input("Office vendor name");
        cut.Find("input[id$='-last']").Input("Different personal surname");
        Assert.Equal("Office vendor name", model.Name);
        cut.Find(".vendor-import-action button").Click();
        Assert.NotNull(cut.Find("[role='alertdialog']"));
        Assert.DoesNotContain("/import", Services.GetRequiredService<NavigationManager>().Uri);
        cut.FindAll("footer button").Single(x => x.TextContent == "Keep editing").Click();
        Assert.Equal("Different personal surname", cut.Find("input[id$='-last']").GetAttribute("value"));
        cut.Find(".vendor-import-action button").Click();
        cut.FindAll("footer button").Single(x => x.TextContent == "Continue").Click();
        Assert.EndsWith("/operations/fish-meat-vendor-fees/import", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal("Office vendor name", model.Name);
    }

    [Fact]
    public void AnUntouchedFormNavigatesWithoutSubmittingOrPrompting()
    {
        var submitted = false;
        var cut = RenderComponent<VendorRegistrationDrawer>(p => p.Add(x => x.Show, true).Add(x => x.ShowImport, true)
            .Add(x => x.Model, new VendorRegistrationForm()).Add(x => x.OnSubmit, () => submitted = true));
        cut.Find(".vendor-import-action button").Click();
        Assert.EndsWith("/operations/fish-meat-vendor-fees/import", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.False(submitted);
        Assert.Empty(cut.FindAll("[role='alertdialog']"));
    }
}
