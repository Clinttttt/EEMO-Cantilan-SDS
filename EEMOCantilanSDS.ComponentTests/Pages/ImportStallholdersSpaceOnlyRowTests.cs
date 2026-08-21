using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ImportStallholders = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.ImportStallholders;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Adding a row for a space the office does not number.
///
/// <para>
/// Reported from use: on a barbecue or ice-plant list, where every occupancy is a space let without a signed
/// contract, Add row handed the new row one of the facility's stall numbers. The office issues no number for
/// these, so the row was filed under a numbering that does not exist, beside rows correctly carrying SP
/// identifiers. The cause was that one button had to assume a basis, and an empty contract cell reads as
/// "per signed contract".
/// </para>
///
/// <para>
/// Two bases, two buttons. And the identifier of a space is not editable: it is what the occupancy is keyed,
/// linked and routed on, so typing "SP-2" into "GE-2" would either collide with the facility's numbering or
/// leave the row identified by nothing. The basis is changed in the contract column; the identifier follows it.
/// </para>
/// </summary>
public class ImportStallholdersSpaceOnlyRowTests : TestContext
{
    private IRenderedComponent<ImportStallholders> Render(string facility)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallHoldersListAsync(It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>()))
              .ReturnsAsync(Result<StallHoldersListDto>.Success(new StallHoldersListDto()));

        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<ImportStallholders>(p => p.Add(c => c.Facility, facility));
    }

    private static List<string> Numbers(IRenderedComponent<ImportStallholders> cut) =>
        cut.FindAll("input.imp-stallno").Select(i => i.GetAttribute("value") ?? string.Empty).ToList();

    /// <summary>
    /// The manual path, then the row it starts with removed — so what each test adds is the only row in the list
    /// and its identifier is unambiguous.
    /// </summary>
    private static void StartEmpty(IRenderedComponent<ImportStallholders> cut)
    {
        cut.Find("button.imp-enter-manually").Click();
        cut.FindAll("button.imp-row-del")[0].Click();
    }

    [Fact]
    public void BothBasesAreOffered()
    {
        var cut = Render("bbq");
        cut.Find("button.imp-enter-manually").Click();

        Assert.NotNull(cut.Find("button.imp-add-row"));
        Assert.NotNull(cut.Find("button.imp-add-space-row"));
    }

    [Fact]
    public void ASpaceRowTakesASpaceIdentifier_NotAStallNumber()
    {
        var cut = Render("bbq");
        StartEmpty(cut);

        cut.Find("button.imp-add-space-row").Click();

        var number = Assert.Single(Numbers(cut));
        Assert.True(SpaceNumber.IsSpace(number), $"expected a space identifier, got '{number}'");
        Assert.Equal(SpaceNumber.Format(1), number);
    }

    [Fact]
    public void SpaceIdentifiersContinueTheirOwnSeries()
    {
        var cut = Render("bbq");
        StartEmpty(cut);

        cut.Find("button.imp-add-space-row").Click();
        cut.Find("button.imp-add-space-row").Click();
        cut.Find("button.imp-add-space-row").Click();

        Assert.Equal(new[] { SpaceNumber.Format(1), SpaceNumber.Format(2), SpaceNumber.Format(3) }, Numbers(cut));
    }

    [Fact]
    public void AContractRowStillTakesTheFacilitysNextStallNumber()
    {
        var cut = Render("tcc");
        StartEmpty(cut);

        cut.Find("button.imp-add-row").Click();

        var number = Assert.Single(Numbers(cut));
        Assert.False(SpaceNumber.IsSpace(number), $"a contracted stall must not carry a space identifier, got '{number}'");
        Assert.Equal("1", number);
    }

    [Fact]
    public void ASpaceRowNeverConsumesAStallNumberFromTheContractedSeries()
    {
        // The two series are independent: a space must not push the counter for stalls the office does number.
        var cut = Render("tcc");
        StartEmpty(cut);

        cut.Find("button.imp-add-space-row").Click();
        cut.Find("button.imp-add-row").Click();
        cut.Find("button.imp-add-space-row").Click();
        cut.Find("button.imp-add-row").Click();

        var numbers = Numbers(cut);
        Assert.Equal(SpaceNumber.Format(1), numbers[0]);
        Assert.Equal("1", numbers[1]);
        Assert.Equal(SpaceNumber.Format(2), numbers[2]);
        Assert.Equal("2", numbers[3]);
    }

    [Fact]
    public void ASpaceRowSaysOnItsFaceThatItHasNoContract()
    {
        // The basis is what the numbering reads, so it has to be visible in the row and not merely implied.
        var cut = Render("bbq");
        StartEmpty(cut);

        cut.Find("button.imp-add-space-row").Click();

        var contractCell = cut.FindAll("input.imp-col-contractname").Single();
        Assert.Contains("no contract", contractCell.GetAttribute("value") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheIdentifierOfASpaceCannotBeTypedOver()
    {
        var cut = Render("bbq");
        StartEmpty(cut);

        cut.Find("button.imp-add-space-row").Click();

        var cell = cut.FindAll("input.imp-stallno").Single();
        Assert.True(cell.HasAttribute("readonly"), "a space identifier must not be editable");
    }

    [Fact]
    public void AContractedStallNumberStaysEditable()
    {
        // The office's own numbering remains the office's to correct.
        var cut = Render("tcc");
        StartEmpty(cut);

        cut.Find("button.imp-add-row").Click();

        var cell = cut.FindAll("input.imp-stallno").Single();
        Assert.False(cell.HasAttribute("readonly"), "a contracted stall number must remain editable");
    }
}
