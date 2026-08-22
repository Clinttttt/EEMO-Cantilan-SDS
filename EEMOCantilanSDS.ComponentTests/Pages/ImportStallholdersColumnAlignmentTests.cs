using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ImportStallholders = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.ImportStallholders;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Which column of an uploaded file lands in which field.
///
/// <para>
/// The import used to match by POSITION alone and discard the file's header. That is correct only while two facilities
/// carry the same columns. The market, Tampak, the Barbecue Stand and the Iceplant do, so a file prepared for one maps
/// onto another. The New Commercial Center does not: it carries "Area Loc." ninth, before Delinquent. So a market or
/// Tampak file read positionally put a money figure into Area Loc. and left Delinquent empty, with nothing to warn the
/// office that a column had shifted.
/// </para>
///
/// <para>
/// The file's own header is now read when it is one of ours, and position remains the fallback for the office's printed
/// reports, whose headings are their own wording. Both are held here.
/// </para>
/// </summary>
public class ImportStallholdersColumnAlignmentTests : TestContext
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

    private static void Upload(IRenderedComponent<ImportStallholders> cut, string csv)
    {
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText(csv, "stallholders.csv", contentType: "text/csv"));
    }

    /// <summary>A cell by the NAME of its column, which is the class the screen puts on it.</summary>
    private static string Cell(IRenderedComponent<ImportStallholders> cut, string key)
    {
        var selector = key == "stallNo" ? "input.imp-stallno" : "input.imp-col-" + key.ToLowerInvariant();
        return cut.Find(selector).GetAttribute("value") ?? string.Empty;
    }

    [Fact]
    public void AFileWhoseColumnsAreInOurOrder_LandsInTheRightFields()
    {
        // The ordinary case, and the one the office's own template produces.
        var cut = Render("tcc");
        Upload(cut, string.Join("\n",
            "Actual Occupant,Name on Contract,Stall / Space No.,Effectivity Date,No. of Years,Area (sqm),Monthly Rental (₱),Actual Mo. Rental (₱),Whole Year Rental (₱),Delinquent (₱)",
            "Joseph Villamor,Joseph Villamor,7,2026-01-01,3,10.5,2400,2400,28800,150"));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input.imp-stallno")), TimeSpan.FromSeconds(10));

        Assert.Equal("7", Cell(cut, "stallNo"));
        Assert.Equal("2400", Cell(cut, "monthly"));
        Assert.Equal("150", Cell(cut, "delinquent"));
    }

    [Fact]
    public void AFileFromAnotherFacility_IsAlignedByItsHeader_NotByPosition()
    {
        // The fault this exists for. A Tampak file has no "Area Loc." at all, so read positionally under the New
        // Commercial Center its Delinquent figure would land in Area Loc. Read by its header, Delinquent stays money and
        // Area Loc. is simply left for the office to fill.
        var cut = Render("ncc");
        Upload(cut, string.Join("\n",
            "Actual Occupant,Name on Contract,Stall / Space No.,Effectivity Date,No. of Years,Area (sqm),Monthly Rental (₱),Actual Mo. Rental (₱),Whole Year Rental (₱),Delinquent (₱)",
            "Lucia Ramirez,Lucia Ramirez,4,2026-01-01,3,10.5,1200,1200,14400,320"));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input.imp-stallno")), TimeSpan.FromSeconds(10));

        Assert.Equal("320", Cell(cut, "delinquent"));
        Assert.Equal("1200", Cell(cut, "monthly"));
    }

    [Fact]
    public void AHeaderInADifferentOrder_IsStillRead()
    {
        // A header is an instruction about order, not decoration. An office that moved a column, or exported from its
        // own sheet in another order, is read correctly rather than shifted.
        var cut = Render("tcc");
        Upload(cut, string.Join("\n",
            "Stall / Space No.,Actual Occupant,Delinquent (₱),Monthly Rental (₱),Name on Contract",
            "9,Marlon Aguilar,75,2400,Marlon Aguilar"));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input.imp-stallno")), TimeSpan.FromSeconds(10));

        Assert.Equal("9", Cell(cut, "stallNo"));
        Assert.Equal("75", Cell(cut, "delinquent"));
        Assert.Equal("2400", Cell(cut, "monthly"));
    }

    [Fact]
    public void AFileWithNoHeaderOfOurs_StillFallsBackToPosition()
    {
        // The office's printed reports carry their own headings, which this cannot recognise. Those must keep working
        // exactly as they did, by position.
        var cut = Render("tcc");
        Upload(cut, string.Join("\n",
            "LIST OF STALLHOLDERS",
            "Joseph Villamor,Joseph Villamor,7,2026-01-01,3,10.5,2400,2400,28800,150"));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input.imp-stallno")), TimeSpan.FromSeconds(10));

        Assert.Equal("7", Cell(cut, "stallNo"));
        Assert.Equal("150", Cell(cut, "delinquent"));
    }
}
