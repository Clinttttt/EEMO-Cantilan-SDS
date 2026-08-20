using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// Whose words name the market's collection areas in the Add Vendor form.
///
/// <para>
/// Reported from use: Madrid named its areas Gulayan, Isda and Karne at onboarding, and its Add Vendor form still
/// offered "Vegetable Area", "Fish Area", "Meat Area" - the reference municipality's wording, hardcoded in the
/// dropdown, on a form a Madrid clerk fills in every time a space changes hands.
/// </para>
///
/// <para>
/// The option VALUES are the canonical keys the form and the server are written against and must not move; only the
/// words shown to the clerk come from the LGU's own facility record. An area the office has not named keeps the
/// platform's canonical wording, which states nothing about anybody.
/// </para>
/// </summary>
public class AddVendorModalSectionLabelTests : TestContext
{
    private string RenderWith(string? vegetable, string? fish, string? meat)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.NamingTheMarket(
            "Madrid Public Market", "MPM", vegetable, fish, meat));

        var cut = RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.Form, new AddVendorModal.VendorModalForm { FacilityCode = "NPM", StallNo = "3" }));

        // The area options are listed only once the clerk opens the dropdown, so open each one.
        for (var i = 0; i < cut.FindAll(".fh-dd-trigger").Count; i++)
            cut.FindAll(".fh-dd-trigger")[i].Click();

        return cut.Markup;
    }

    [Fact]
    public void TheOfficesOwnNamesForItsAreasAreOffered()
    {
        var markup = RenderWith("Gulayan", "Isda", "Karne");

        Assert.Contains("Gulayan", markup);
        Assert.Contains("Isda", markup);
        Assert.Contains("Karne", markup);
    }

    [Fact]
    public void CANTILANSWordingIsNeverOfferedToAnotherLGU()
    {
        // The defect. The keys stay Vegetable/Fish/Meat either way; the words the clerk reads must not.
        var markup = RenderWith("Gulayan", "Isda", "Karne");

        Assert.DoesNotContain("Vegetable Area", markup);
        Assert.DoesNotContain("Fish Area", markup);
        Assert.DoesNotContain("Meat Area", markup);
    }

    [Fact]
    public void AnAreaTheOfficeHasNotNamedKeepsTheCanonicalWording()
    {
        var markup = RenderWith(null, "Isda", null);

        Assert.Contains("Isda", markup);
        Assert.Contains("Vegetable Area", markup);
        Assert.Contains("Meat Area", markup);
    }
}
