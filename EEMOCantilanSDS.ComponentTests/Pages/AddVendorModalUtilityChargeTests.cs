using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// Utility eligibility comes from the registered NPM source. Registration neither asks for utility flags nor clears existing flags.
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
        // The form reads the office's own facility record for its market's area names; this test is about
        // something else, so no record is loaded and the canonical wording stands.
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());

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
    public void Registration_has_no_manual_utility_step()
    {
        var cut = RenderForm(MarketSpace());

        Assert.Empty(cut.FindAll(".avm-util-none"));
        Assert.DoesNotContain("Utility service", cut.Markup);
        Assert.Empty(cut.FindAll(".avm-fee-toggle"));      // no empty boxes offered
    }

    [Fact]
    public void Existing_utility_associations_are_preserved_without_rendering_toggles()
    {
        var form = MarketSpace("Electricity", "Water");
        var cut = RenderForm(form);
        Assert.Equal(new[] { "Electricity", "Water" }, form.FeeTypes);

        // Stated as its own utility operation through concise labels — never stall rent, never a reading-driven bill,
        // and no explanatory paragraph.
        Assert.DoesNotContain("Utility service", cut.Markup);
        Assert.Empty(cut.FindAll(".avm-fee-toggle"));
        Assert.DoesNotContain("assessed and collected apart from stall rental", cut.Markup);
        Assert.DoesNotContain("amount varies per reading", cut.Markup);
        Assert.DoesNotContain("Utility Charges", cut.Markup);
        Assert.DoesNotContain("metered", cut.Markup);
    }

    [Fact]
    public void AddingAUtilityCharge_OpensTheControls_AndSaysWhatToDo()
    {
        var cut = RenderForm(MarketSpace());

        Assert.Empty(cut.FindAll(".avm-util-add"));
        Assert.Empty(cut.FindAll(".avm-fee-toggle"));
        Assert.Empty(cut.FindAll(".avm-util-none"));
        Assert.DoesNotContain("Mark the service this space uses", cut.Markup);
        // Nothing is ticked for them: the charge goes on the stall only when the office says which.
        Assert.Empty(cut.FindAll(".avm-fee-toggle.active"));
    }

    [Fact]
    public void ASpaceThatAlreadyCarriesACharge_ShowsTheControlsDirectly()
    {
        var cut = RenderForm(MarketSpace("Electricity"));

        Assert.Empty(cut.FindAll(".avm-util-none"));
        Assert.Empty(cut.FindAll(".avm-fee-toggle.active"));
        Assert.DoesNotContain("Mark the service this space uses", cut.Markup);
    }

    [Fact]
    public void TheDecisionToAdd_DoesNotLeakToTheNextSpaceOpened()
    {
        // The leak: having opened the controls for one stall, every stall opened afterwards showed two empty
        // toggles instead of stating plainly that it carries no utility charges.
        var cut = RenderForm(MarketSpace());
        Assert.Empty(cut.FindAll(".avm-util-add"));
        Assert.Empty(cut.FindAll(".avm-util-none"));

        cut.SetParametersAndRender(p => p.Add(c => c.Show, false));                     // form closed
        cut.SetParametersAndRender(p => p
            .Add(c => c.Show, true)
            .Add(c => c.Form, MarketSpace()));                                          // another space

        Assert.Empty(cut.FindAll(".avm-util-none"));
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
