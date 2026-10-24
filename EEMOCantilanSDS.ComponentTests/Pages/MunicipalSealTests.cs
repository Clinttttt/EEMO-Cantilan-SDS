using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Pages.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class MunicipalSealTests : TestContext
{
    public MunicipalSealTests()
    {
        // The project's global Razor imports inject BrandingState into every component.
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton<BrandingState>();
    }

    [Fact]
    public void Healthy_seal_keeps_its_exact_versioned_url()
    {
        const string seal = "https://api.stalltrack.site/api/municipalities/carrascal/seal?v=abc";
        var component = RenderComponent<MunicipalSeal>(p => p
            .Add(x => x.Source, seal).Add(x => x.Municipality, "Carrascal"));

        Assert.Equal(seal, component.Find("img").GetAttribute("src"));
        Assert.Equal("Carrascal seal", component.Find("img").GetAttribute("alt"));
        Assert.Single(component.FindAll(".municipal-seal-outline"));
    }

    [Fact]
    public void Failed_request_shows_neutral_outline_without_a_broken_image_or_an_infinite_retry()
    {
        const string seal = "https://api.stalltrack.site/api/municipalities/carrascal/seal?v=abc";
        var errors = new List<string>();
        var component = RenderComponent<MunicipalSeal>(p => p
            .Add(x => x.Source, seal).Add(x => x.Municipality, "Carrascal")
            .Add(x => x.OnFailed, (string url) => errors.Add(url)));

        component.Find("img").TriggerEvent("onerror", EventArgs.Empty);

        Assert.Empty(component.FindAll("img"));
        Assert.Single(component.FindAll(".municipal-seal-outline"));
        Assert.Equal([seal], errors);

        component.Render();
        Assert.Empty(component.FindAll("img"));
        Assert.Single(errors);
    }

    [Fact]
    public void Switching_municipalities_never_keeps_the_previous_seal()
    {
        var component = RenderComponent<MunicipalSeal>(p => p
            .Add(x => x.Source, "https://example.test/carrascal.jpg")
            .Add(x => x.Municipality, "Carrascal"));

        component.SetParametersAndRender(p => p
            .Add(x => x.Source, (string?)null)
            .Add(x => x.Municipality, "Madrid"));

        Assert.Empty(component.FindAll("img"));
        Assert.Single(component.FindAll(".municipal-seal-outline"));

        component.SetParametersAndRender(p => p
            .Add(x => x.Source, "https://example.test/madrid.jpg")
            .Add(x => x.Municipality, "Madrid"));

        Assert.Equal("https://example.test/madrid.jpg", component.Find("img").GetAttribute("src"));
        Assert.Equal("Madrid seal", component.Find("img").GetAttribute("alt"));
    }

    [Fact]
    public void Updated_version_recovers_after_a_failed_previous_image()
    {
        var component = RenderComponent<MunicipalSeal>(p => p.Add(x => x.Source, "/carrascal?v=1"));
        component.Find("img").TriggerEvent("onerror", EventArgs.Empty);
        Assert.Empty(component.FindAll("img"));

        component.SetParametersAndRender(p => p.Add(x => x.Source, "/carrascal?v=2"));
        Assert.Equal("/carrascal?v=2", component.Find("img").GetAttribute("src"));
    }
}
