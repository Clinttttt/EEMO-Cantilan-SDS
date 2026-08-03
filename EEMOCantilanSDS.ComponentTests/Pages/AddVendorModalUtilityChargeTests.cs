using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// Adding a utility charge to a space that has none. The office installs a meter on a stall, adds the charge on
/// the vendor form, and the meter-reading dialog then offers that utility — so what this form saves is what the
/// rest of the system bills. These tests pin the form's side of that: the offer to add appears only for a space
/// with no charges, opening it reveals the two controls, and the decision does not leak to the next space opened.
/// </summary>
public class AddVendorModalUtilityChargeTests : TestContext
{
    private IRenderedComponent<AddVendorModal> RenderForm(AddVendorModal.VendorModalForm form, bool isEditing = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.IsEditing, isEditing)
            .Add(c => c.Form, form)
            .Add(c => c.NpmFishRate, 1m));
    }

    private static AddVendorModal.VendorModalForm MarketSpace(params string[] fees) => new()
    {
        FacilityCode = "NPM",
        StallNo = "3",
        SelectedSection = "Vegetables",
        FeeTypes = fees.ToList()
    };

    [Fact]
    public void ASpaceWithNoUtilities_SaysSo_AndOffersToAddOne()
    {
        var cut = RenderForm(MarketSpace());

        Assert.Single(cut.FindAll(".avm-util-none"));
        Assert.Contains("No utility charges on this space.", cut.Markup);
        Assert.Empty(cut.FindAll(".avm-fee-toggle"));      // no empty boxes offered
    }

    [Fact]
    public void AddingAUtilityCharge_OpensTheControls_AndSaysWhatToDo()
    {
        var cut = RenderForm(MarketSpace());

        cut.Find(".avm-util-add").Click();

        Assert.Equal(2, cut.FindAll(".avm-fee-toggle").Count);   // electricity and water
        Assert.Empty(cut.FindAll(".avm-util-none"));
        Assert.Contains("Tick the service this space is metered for", cut.Markup);
        // Nothing is ticked for them: the charge goes on the stall only when the office says which.
        Assert.Empty(cut.FindAll(".avm-fee-toggle.active"));
    }

    [Fact]
    public void ASpaceThatAlreadyCarriesACharge_ShowsTheControlsDirectly()
    {
        var cut = RenderForm(MarketSpace("Electricity"));

        Assert.Empty(cut.FindAll(".avm-util-none"));
        Assert.Single(cut.FindAll(".avm-fee-toggle.active"));
        Assert.DoesNotContain("Tick the service this space is metered for", cut.Markup);
    }

    [Fact]
    public void TheDecisionToAdd_DoesNotLeakToTheNextSpaceOpened()
    {
        // The leak: having opened the controls for one stall, every stall opened afterwards showed two empty
        // toggles instead of stating plainly that it carries no utility charges.
        var cut = RenderForm(MarketSpace());
        cut.Find(".avm-util-add").Click();
        Assert.Empty(cut.FindAll(".avm-util-none"));

        cut.SetParametersAndRender(p => p.Add(c => c.Show, false));                     // form closed
        cut.SetParametersAndRender(p => p
            .Add(c => c.Show, true)
            .Add(c => c.Form, MarketSpace()));                                          // another space

        Assert.Single(cut.FindAll(".avm-util-none"));
        Assert.Empty(cut.FindAll(".avm-fee-toggle"));
    }

    [Fact]
    public void TheSectionFeeToken_CarriesNoRate()
    {
        // A token is a key, not a price: an LGU with its own per-kilo rate must not inherit another's figure
        // through a magic string.
        Assert.Equal("Section fee", AddVendorModal.SectionFeeType);
        Assert.DoesNotContain("₱", AddVendorModal.SectionFeeType);
    }
}
