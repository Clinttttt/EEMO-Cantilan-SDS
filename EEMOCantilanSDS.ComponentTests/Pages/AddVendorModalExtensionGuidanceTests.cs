using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// The warning shown when a vendor is registered on the "no contract (extension)" basis.
///
/// <para>
/// This form CREATES a space; it cannot attach to one that is already let. <c>CreateStallCommandHandler</c> will reuse an
/// existing stall only where that stall is VACANT, and a stall whose lessee is still trading on a lapsed contract is not.
/// So choosing this basis for a stall that already has a number does not record that stall's extension — it registers a
/// second, un-numbered space in the same lessee's name and splits their history and arrears across the two.
/// </para>
///
/// <para>
/// The basis is deliberately KEPT rather than hidden, because the office does record un-numbered spaces held on an
/// extension — a commercial-centre space whose lapsed contract may exist only on paper, never in this system. Hiding it on
/// the facilities that number their stalls would have removed exactly that case. The office is told what the choice does
/// instead, and pointed at renewal, which keeps the number and carries the lessee's name forward so it cannot be mistyped.
/// </para>
/// </summary>
public class AddVendorModalExtensionGuidanceTests : TestContext
{
    private IRenderedComponent<AddVendorModal> RenderForm(AddVendorModal.VendorModalForm form)
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
            .Add(c => c.IsEditing, false)
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

    [Fact]
    public void TheExtensionBasis_SaysItRegistersANewUnnumberedSpace_AndPointsAtRenewal()
    {
        var cut = RenderForm(Form(OccupancyArrangement.Extension));

        var markup = cut.Markup;

        Assert.Contains("no stall number", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("renew", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASignedContract_IsNotWarned()
    {
        // The guidance must speak only to the basis it is about. A signed contract takes the facility's own stall number and
        // is the ordinary case; warning there would train the office to ignore it.
        var cut = RenderForm(Form(OccupancyArrangement.SignedContract));

        Assert.Empty(cut.FindAll(".avm-info-box-title"));
    }

    [Fact]
    public void ASpaceOnlyOccupancy_IsNotWarned()
    {
        // A space let with no contract at all is a genuinely new un-numbered space — a barbecue stand, an ice-plant space —
        // so there is no existing stall it could have been confused with.
        var cut = RenderForm(Form(OccupancyArrangement.SpaceOnly));

        Assert.Empty(cut.FindAll(".avm-info-box-title"));
    }
}
