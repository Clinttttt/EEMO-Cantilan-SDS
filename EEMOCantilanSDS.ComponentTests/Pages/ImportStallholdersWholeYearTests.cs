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
/// Whole Year Rental on the stallholder import, and the line the screen has to hold.
///
/// <para>
/// The figure is never saved: arrears are computed from the term and the payments, and whole-year rental is derived from
/// the rent, so storing it would state the same money twice. It is read in exactly one place, to CHECK the office's own
/// arithmetic - which is how a three-year term stating 37 months' rent announced itself.
/// </para>
///
/// <para>
/// So the two cases must behave differently, and that is the whole of this file. On an UPLOADED row the figure is the
/// office's evidence and is left exactly as their paper says, or the check would compare a number with itself and always
/// agree. On a row invented HERE there is no paper behind it, so the screen does the multiplication the office would
/// otherwise do by hand - and stops the moment the clerk states a figure themselves.
/// </para>
/// </summary>
public class ImportStallholdersWholeYearTests : TestContext
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

    private static string WholeYear(IRenderedComponent<ImportStallholders> cut, int row = 0) =>
        cut.FindAll("input.imp-col-wholeyear")[row].GetAttribute("value") ?? string.Empty;

    /// <summary>Starts a hand-entered batch, which is the case that derives.</summary>
    private IRenderedComponent<ImportStallholders> Manual(string facility = "tcc")
    {
        var cut = Render(facility);
        cut.Find("button.imp-enter-manually").Click();
        return cut;
    }

    [Fact]
    public void TypingTheRentFillsTwelveMonthsOfItOnAHandEnteredRow()
    {
        var cut = Manual();

        cut.FindAll("input.imp-col-monthly")[0].Input("900");

        Assert.Equal("10800", WholeYear(cut));
    }

    [Fact]
    public void TheACTUALRentWinsWhereOneIsStated()
    {
        // The same precedence the reconciliation check uses, so the derived figure agrees with the way the office reads
        // the row rather than with the contract rate it may have outgrown.
        var cut = Manual();

        cut.FindAll("input.imp-col-monthly")[0].Input("900");
        cut.FindAll("input.imp-col-actualmonthly")[0].Input("1500");

        Assert.Equal("18000", WholeYear(cut));
    }

    [Fact]
    public void ChangingTheRentKeepsTheWholeYearInStep()
    {
        var cut = Manual();

        cut.FindAll("input.imp-col-monthly")[0].Input("900");
        cut.FindAll("input.imp-col-monthly")[0].Input("2400");

        Assert.Equal("28800", WholeYear(cut));
    }

    [Fact]
    public void WithNoRentYetTheCellIsLeftBlankRatherThanReadingZero()
    {
        // A row still being typed states no rent, and "0" in a money column is a figure nobody meant.
        var cut = Manual();

        Assert.Equal(string.Empty, WholeYear(cut));

        cut.FindAll("input.imp-col-monthly")[0].Input("0");
        Assert.Equal(string.Empty, WholeYear(cut));
    }

    [Fact]
    public void ONCETheClerkStatesTheFigureTheScreenStopsWritingOverIt()
    {
        // The same rule the stall-number cell already follows: typing a value makes it theirs. Without this, the next
        // keystroke in the rent column would erase what they had just entered.
        var cut = Manual();

        cut.FindAll("input.imp-col-monthly")[0].Input("900");
        Assert.Equal("10800", WholeYear(cut));

        cut.FindAll("input.imp-col-wholeyear")[0].Input("11000");     // the office knows something the screen does not
        cut.FindAll("input.imp-col-monthly")[0].Input("950");

        Assert.Equal("11000", WholeYear(cut));
    }

    [Fact]
    public void EveryROWAddedByHandDerivesItsOwn()
    {
        var cut = Manual();
        cut.Find("button.imp-add-row").Click();

        cut.FindAll("input.imp-col-monthly")[1].Input("1200");

        Assert.Equal("14400", WholeYear(cut, 1));
        Assert.Equal(string.Empty, WholeYear(cut, 0));   // untouched, and still its own
    }

    [Fact]
    public void ASAMPLEROWKeepsTheFigureTheSampleStates()
    {
        // The sample teaches the FORMAT of the office's list, including that column, so it must read like a sheet and
        // not like something this screen worked out.
        var cut = Render("tcc");
        cut.Find("button.imp-use-sample").Click();

        var stated = WholeYear(cut);
        Assert.False(string.IsNullOrWhiteSpace(stated));

        // Edited on the ACTUAL rate, which is the rate this row's figure is twelve months of - so a screen that had
        // "helpfully" recomputed the cell would visibly change it here. Editing the contract rate instead would prove
        // nothing, because the actual rate takes precedence and the derived figure would come out the same.
        cut.FindAll("input.imp-col-actualmonthly")[0].Input("1");
        Assert.Equal(stated, WholeYear(cut));
    }

    [Fact]
    public void AROWAddedToASHEETDerivesWhileTheSheetsOwnRowsDoNot()
    {
        // The provenance rule at its sharpest: both kinds of row sit in the same table at the same time. The office's
        // rows keep their stated figures because they are evidence; the row added underneath them is the screen's to
        // work out. Getting this wrong in either direction is the whole risk - rewriting the office's cell would retire
        // the check that caught a three-year term stating 37 months' rent, and not deriving the new one leaves the clerk
        // multiplying by twelve for a figure that is then discarded.
        var cut = Render("tcc");
        cut.Find("button.imp-use-sample").Click();

        var sampleFigures = cut.FindAll("input.imp-col-wholeyear")
                               .Select(i => i.GetAttribute("value") ?? string.Empty).ToList();
        var added = sampleFigures.Count;                     // the new row is last

        cut.Find("button.imp-add-row").Click();
        cut.FindAll("input.imp-col-monthly")[added].Input("2400");

        Assert.Equal("28800", WholeYear(cut, added));

        // and not one of the office's own figures moved
        Assert.Equal(sampleFigures,
            cut.FindAll("input.imp-col-wholeyear").Take(sampleFigures.Count)
               .Select(i => i.GetAttribute("value") ?? string.Empty).ToList());
    }

    [Fact]
    public void TheSHEETSFiguresAreCheckedAsLOADEDRatherThanAsRecomputed()
    {
        // Worth stating, because it is why the two cases can coexist safely: the reconciliation reads the office's sheet
        // once, when the batch is loaded. The sample's own figures agree with its rents, so nothing is raised - and they
        // still agree afterwards, because editing a rent on a sheet row leaves its stated figure alone.
        var cut = Render("tcc");
        cut.Find("button.imp-use-sample").Click();

        Assert.DoesNotContain("whole year states", cut.Markup);

        var stated = WholeYear(cut);
        cut.FindAll("input.imp-col-actualmonthly")[0].Input("123");

        Assert.Equal(stated, WholeYear(cut));
    }

    [Fact]
    public void ADerivedRowIsNeverQueriedAboutItsOwnArithmetic()
    {
        // The other half: the office is not asked to check a multiplication this screen just performed.
        var cut = Manual();

        cut.FindAll("input.imp-col-monthly")[0].Input("2400");

        Assert.DoesNotContain("whole year states", cut.Markup);
    }
}
