using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// What identifier the form gives a space, according to how the space is held.
///
/// <para>
/// The office numbers the stalls it lets under a signed contract. It does NOT number a space let without one — a
/// barbecue stand, an ice-plant space, a commercial-centre space held on an extension — and its own list leaves that
/// column blank. The form used to ask for a stall number regardless and suggest the facility's next one, so an
/// un-numbered space was recorded as stall 4 and the actual stall 4 could no longer be registered: the number came
/// back reported as occupied by an active contract.
/// </para>
/// </summary>
public class AddVendorModalSpaceNumberingTests : TestContext
{
    private IRenderedComponent<AddVendorModal> RenderForm(
        AddVendorModal.VendorModalForm form,
        IReadOnlyCollection<string>? existing = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.IsEditing, false)
            .Add(c => c.Form, form)
            .Add(c => c.ExistingStallNos, existing ?? Array.Empty<string>())
            .Add(c => c.NpmFishRate, 1m));
    }

    private static AddVendorModal.VendorModalForm CommercialSpace() => new()
    {
        FacilityCode = "TCC",
        Arrangement = OccupancyArrangement.SignedContract,
        FeeTypes = new List<string>()
    };

    [Fact]
    public void ASignedContract_IsAskedForAStallNumber()
    {
        var cut = RenderForm(CommercialSpace(), new[] { "1", "2", "3" });

        // The editable field, and a number continuing the facility's own series.
        Assert.NotEmpty(cut.FindAll(".avm-input"));
        Assert.Equal("4", cut.Instance.Form.StallNo);
        Assert.Empty(cut.FindAll(".avm-readonly"));
    }

    [Fact]
    public void ChoosingNoContract_TakesTheSpaceSeries_AndStopsAskingForADigit()
    {
        var cut = RenderForm(CommercialSpace(), new[] { "1", "2", "3" });

        // The clerk switches the basis of occupancy to a space held without a contract.
        cut.FindAll(".avm-choice-item")[1].Click();

        // No stall number is taken from the facility: 4 remains free for the actual stall 4.
        Assert.Equal("SP-1", cut.Instance.Form.StallNo);
        Assert.False(SpaceNumber.IsSpace("4"));
        Assert.True(SpaceNumber.IsSpace(cut.Instance.Form.StallNo));

        // And the clerk is no longer asked to type one — the office issues none for such a space.
        Assert.Single(cut.FindAll(".avm-readonly"));
        Assert.Contains("Not a numbered stall", cut.Markup);

        // The identifier is assigned behind the form but never shown: the office's own list has no number for these,
        // so putting one in front of the clerk would invite them to treat it as one.
        Assert.DoesNotContain("SP-1", cut.Markup);
    }

    [Fact]
    public void TheSpaceSeriesContinuesPastSpacesTheFacilityAlreadyHas()
    {
        var cut = RenderForm(CommercialSpace(), new[] { "1", "2", "SP-1", "SP-2" });

        cut.FindAll(".avm-choice-item")[1].Click();

        // Continues at SP-3 rather than restarting over a space already recorded.
        Assert.Equal("SP-3", cut.Instance.Form.StallNo);
    }

    [Fact]
    public void SwitchingBackToASignedContract_RestoresAStallNumber()
    {
        var cut = RenderForm(CommercialSpace(), new[] { "1", "2", "3" });

        cut.FindAll(".avm-choice-item")[1].Click();
        Assert.Equal("SP-1", cut.Instance.Form.StallNo);

        cut.FindAll(".avm-choice-item")[0].Click();

        // A signed contract must not be saved against a space identifier.
        Assert.Equal("4", cut.Instance.Form.StallNo);
        Assert.False(SpaceNumber.IsSpace(cut.Instance.Form.StallNo));
        Assert.Empty(cut.FindAll(".avm-readonly"));
    }

    [Fact]
    public void ASpaceIdentifierNeverEqualsAStallNumberTheOfficeCouldIssue()
    {
        // The property the whole change rests on, stated where a reader of this form will find it.
        var cut = RenderForm(CommercialSpace(), new[] { "1", "2", "3" });
        cut.FindAll(".avm-choice-item")[1].Click();

        var assigned = cut.Instance.Form.StallNo;
        foreach (var officeNumber in new[] { "1", "2", "3", "4", "01", "101" })
            Assert.NotEqual(officeNumber, assigned);
    }
}
