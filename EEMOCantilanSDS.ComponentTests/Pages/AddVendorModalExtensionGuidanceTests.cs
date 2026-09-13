using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// Which bases of occupancy this form OFFERS, and why an extension is not one of them when registering a vendor.
///
/// <para>
/// An extension means occupying past a LAPSED contract. This form creates a space rather than attaching to one —
/// <c>CreateStallCommandHandler</c> reuses an existing stall only where that stall is vacant, which a stall whose lessee is
/// still trading is not. So choosing it here never recorded an existing stall's extension: it registered a second,
/// un-numbered space in the same lessee's name and split that lessee's history and arrears across the two. The office's own
/// route is Closed Accounts, which offers Renew on a lapsed account and keeps the stall's number while carrying the name
/// forward, so the name is never retyped and cannot be misspelled.
/// </para>
///
/// <para>
/// It must still appear when EDITING a record that already carries it. The basis selector always renders, in add and edit
/// mode alike, so filtering it out unconditionally would show an existing extension — there is one live on NPM stall 6 —
/// with no basis selected at all: missing data on a government form, and a silent change of basis one click away.
/// </para>
/// </summary>
public class AddVendorModalExtensionGuidanceTests : TestContext
{
    private IRenderedComponent<AddVendorModal> RenderForm(AddVendorModal.VendorModalForm form, bool editing = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());

        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.IsEditing, editing)
            .Add(c => c.Form, form)
            .Add(c => c.ExistingStallNos, new[] { "1", "2", "3" })
            .Add(c => c.NpmFishRate, 1m));
    }

    private static AddVendorModal.VendorModalForm Form(OccupancyArrangement arrangement) => new()
    {
        FacilityCode = "TCC",
        Arrangement = arrangement,
        FeeTypes = new List<string>(),
    };

    private static IReadOnlyList<string> OfferedBases(IRenderedComponent<AddVendorModal> cut) =>
        cut.FindAll(".avm-choice-item .avm-choice-title").Select(e => e.TextContent.Trim()).ToList();

    [Fact]
    public void RegisteringAVendor_DoesNotOfferTheExtensionBasis()
    {
        var offered = OfferedBases(RenderForm(Form(OccupancyArrangement.SignedContract)));

        Assert.DoesNotContain("No contract (extension)", offered);
    }

    [Fact]
    public void RegisteringAVendor_StillOffersTheOtherTwoBases()
    {
        // Removing one basis must not remove the others: a signed lease is the ordinary case, and a space let with no
        // contract at all — a barbecue stand, an ice-plant space — is a genuinely new un-numbered space.
        var offered = OfferedBases(RenderForm(Form(OccupancyArrangement.SignedContract)));

        Assert.Contains("Signed lease contract", offered);
        Assert.Contains("No contract (space only)", offered);
    }

    [Fact]
    public void EditingARecordThatIsAlreadyAnExtension_StillOffersIt()
    {
        // NPM stall 6 is in exactly this state. Hiding the basis here would render the form with none of the three selected.
        var offered = OfferedBases(RenderForm(Form(OccupancyArrangement.Extension), editing: true));

        Assert.Contains("No contract (extension)", offered);
    }

    [Fact]
    public void AnExistingExtension_AlwaysHasItsOwnBasisSelected()
    {
        // The invariant the filter must never break: whatever the form is showing, the record's own basis is on screen and
        // marked as chosen. Asserted through the ARIA state, which is what a screen reader and the office both rely on.
        //
        // aria-checked was bound straight to a bool, and Blazor MINIMISES a bool attribute: the chosen option rendered
        // aria-checked="" and the others carried no aria-checked at all. ARIA requires the literal "true" or "false", so the
        // selected basis was not conveyed to a screen reader on a government form. Two buttons in this same file already used
        // the explicit form; the radios now do too.
        var cut = RenderForm(Form(OccupancyArrangement.Extension), editing: true);

        var states = cut.FindAll(".avm-choice-item")
            .Select(e => e.GetAttribute("aria-checked"))
            .ToList();

        Assert.All(states, s => Assert.True(s is "true" or "false", $"aria-checked must be \"true\" or \"false\", got '{s ?? "absent"}'"));
        Assert.Single(states, s => s == "true");

        var checkedTitles = cut.FindAll(".avm-choice-item[aria-checked=true] .avm-choice-title")
            .Select(e => e.TextContent.Trim())
            .ToList();

        Assert.Equal(new[] { "No contract (extension)" }, checkedTitles);
    }

    [Fact]
    public void RegisteringAVendor_ShowsTwoColumnsForTheTwoBasesOffered()
    {
        // The grid was fixed at three columns, so dropping a basis left an empty cell and a band of whitespace beside the
        // choices. The count is stated by the markup because it is not constant.
        var cut = RenderForm(Form(OccupancyArrangement.SignedContract));

        Assert.Equal(2, OfferedBases(cut).Count);
        Assert.Contains("--avm-choice-count: 2", cut.Find(".avm-choice").GetAttribute("style"));
    }

    [Fact]
    public void EditingAnExtension_ShowsThreeColumnsForTheThreeBasesOffered()
    {
        var cut = RenderForm(Form(OccupancyArrangement.Extension), editing: true);

        Assert.Equal(3, OfferedBases(cut).Count);
        Assert.Contains("--avm-choice-count: 3", cut.Find(".avm-choice").GetAttribute("style"));
    }
}
