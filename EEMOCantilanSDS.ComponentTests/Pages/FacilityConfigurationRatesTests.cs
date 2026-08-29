using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Facilities.UpdateFacility;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using FacilityConfiguration = EEMOCantilanSDS.Client.Components.Pages.Menus.FacilityConfiguration;

/// <summary>
/// How the market's rates read, and what the form offers where the office has stated nothing.
///
/// <para>
/// Two faults the office found by looking at the screen. The per-area rows rendered the stored amount, so an area nobody
/// had priced read "₱0.00" - which reads as free, and is not what happens: an unstated area is billed the market's own
/// daily stall fee, through the one fee rule. And a metered utility's field opened at nought, which is a poor thing to
/// hand a clerk when nought is also a real answer meaning "type the amount on each bill".
/// </para>
/// <para>
/// The rule these hold either side of both fixes: a figure is STATED while the drawer is a record, and only OFFERED once
/// the office has pressed Edit rates. Nothing on this screen writes a rate before Save.
/// </para>
/// </summary>
public class FacilityConfigurationRatesTests : TestContext
{
    private const string Npm = "NPM";

    private Mock<IFacilitiesApiClient> _api = new();

    /// <summary>Opens the market's configuration drawer with the rates an office would actually have.</summary>
    private IRenderedComponent<FacilityConfiguration> RenderDrawer(params ConfiguredRateDto[] rates)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api = new Mock<IFacilitiesApiClient>();
        _api.Setup(a => a.GetFacilityConfigurationAsync())
            .ReturnsAsync(Result<FacilityConfigurationDto>.Success(new FacilityConfigurationDto(
                new List<ConfiguredFacilityDto>
                {
                    new(Npm, "New Public Market", "NPM", null, "Daily stall rental",
                        true, 0, rates.ToList()),
                },
                new List<AvailableFacilityDto>())));
        _api.Setup(a => a.GetNpmCustomSectionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<NpmCustomSectionDto>>.Success(new List<NpmCustomSectionDto>()));
        _api.Setup(a => a.SetFacilityRateAsync(It.IsAny<FacilityCode>(), It.IsAny<FeeRateKey>(), It.IsAny<decimal>()))
            .ReturnsAsync(Result<bool>.Success(true));
        _api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
            {
                new(FacilityCode.NPM, "New Public Market", "NPM", 0),
            }));

        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ITpmApiClient>());
        Services.AddSingleton(Mock.Of<ITrmApiClient>());
        Services.AddSingleton(Mock.Of<ISlaughterApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");

        var cut = RenderComponent<FacilityConfiguration>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".fac-card")));
        cut.Find(".fac-card").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-drawer")));

        return cut;
    }

    private static ConfiguredRateDto Rate(FeeRateKey key, decimal amount, bool stated = false) =>
        new(key.ToString(), key.ToString(), amount, stated);

    private static void PressEditRates(IRenderedComponent<FacilityConfiguration> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Contains("Edit rates")).Click();

    private static void PressEditNames(IRenderedComponent<FacilityConfiguration> cut) =>
        cut.FindAll("button").First(b => b.TextContent.Contains("Edit names")).Click();

    // ── The three canonical area names ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAreaTheOfficeHasNotRenamedStatesTheNameInUseRatherThanReadingBlank()
    {
        // It used to show the office's own name as a value and the platform's as a grey placeholder, so an area nobody had
        // renamed read as an empty field - which says "no name" where it means "called the default".
        var cut = RenderDrawer();

        var names = cut.FindAll(".cfg-name-value").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Vegetable Area", "Fish Area", "Meat Area" }, names);
        Assert.Equal(3, cut.FindAll(".cfg-name-tag").Count);   // each one marked as the default
    }

    [Fact]
    public void TheNamesAreStatedAndNotOfferedForTypingUntilTheOfficeSaysSo()
    {
        var cut = RenderDrawer();

        Assert.Empty(cut.FindAll("input.fac-input[maxlength='60']"));

        PressEditNames(cut);

        // Three fields, and the record they replaced is gone.
        Assert.Equal(3, cut.FindAll("input.fac-input[maxlength='60']").Count);
        Assert.Empty(cut.FindAll(".cfg-name-value"));
    }

    [Fact]
    public void EditingTheNamesLeavesTheFieldsEmptyWhereTheOfficeHasStatedNothing()
    {
        // Empty, not pre-filled with the platform's word. Pre-filling would store the default AS the office's own name on
        // the next Save, which is the same fault as stamping a resolved fee onto a stall's own rate.
        var cut = RenderDrawer();

        PressEditNames(cut);

        var values = cut.FindAll("input.fac-input[maxlength='60']").Select(e => e.GetAttribute("value")).ToList();
        Assert.All(values, v => Assert.True(string.IsNullOrEmpty(v)));
        // The name in use is still visible, as the field's own placeholder.
        Assert.Contains(cut.FindAll("input.fac-input[maxlength='60']"),
            e => e.GetAttribute("placeholder") == "Vegetable Area");
    }

    [Fact]
    public void NothingIsSavedMerelyByOpeningTheNamesForm()
    {
        var cut = RenderDrawer();

        PressEditNames(cut);

        _api.Verify(a => a.UpdateFacilityAsync(It.IsAny<UpdateFacilityCommand>()), Times.Never);
    }

    [Fact]
    public void EveryOpenStartsAsARecordRatherThanAsWhateverWasLeftOpen()
    {
        // The drawer is closed and reopened. A form left open from last time would invite a clerk who came to read.
        var cut = RenderDrawer(Rate(FeeRateKey.NpmDailyStall, 30m, stated: true));
        PressEditNames(cut);
        PressEditRates(cut);

        cut.Find(".fac-card").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-name-value")));
        Assert.Empty(cut.FindAll(".cfg-rate-input input"));
    }

    // ── An area the office has not priced ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAreaTheOfficeHasNotPricedStatesTheMarketsOwnFeeRatherThanNought()
    {
        var cut = RenderDrawer(
            Rate(FeeRateKey.NpmDailyStall, 30m, stated: true),
            Rate(FeeRateKey.NpmDailyStallVegetable, 0m),
            Rate(FeeRateKey.NpmDailyStallFish, 0m),
            Rate(FeeRateKey.NpmDailyStallMeat, 0m));

        // The area group only, since the market's own row states ₱30 as well.
        var areaGroup = cut.FindAll(".cfg-rate-list")[1];
        var rows = areaGroup.QuerySelectorAll(".cfg-rate-value").Select(e => e.TextContent.Trim()).ToList();

        // Three areas, each billed the market's ₱30, and not one of them reading as free.
        Assert.Equal(new[] { "₱30", "₱30", "₱30" }, rows);
        Assert.DoesNotContain(rows, v => v == "₱0" || v == "₱0.00");
    }

    [Fact]
    public void AnAreaWithItsOwnFeeStatesItsOwn()
    {
        var cut = RenderDrawer(
            Rate(FeeRateKey.NpmDailyStall, 30m, stated: true),
            Rate(FeeRateKey.NpmDailyStallFish, 60m, stated: true),
            Rate(FeeRateKey.NpmDailyStallVegetable, 0m));

        var rows = cut.FindAll(".cfg-rate-value").Select(e => e.TextContent.Trim()).ToList();

        Assert.Contains("₱60", rows);   // the fish area's own
        Assert.Contains("₱30", rows);   // the vegetable area, following the market
    }

    [Fact]
    public void WhereTheMarketItselfHasNoStatedFeeTheRowSaysSoInsteadOfShowingAFigure()
    {
        // An office part-way through onboarding. Inventing a figure here would be this platform pricing a stall.
        var cut = RenderDrawer(
            Rate(FeeRateKey.NpmDailyStall, 0m),
            Rate(FeeRateKey.NpmDailyStallVegetable, 0m));

        Assert.Contains("Not set", cut.Markup);
    }

    // ── A metered utility nobody has priced ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AMeteredRateIsStatedAsTheOfficeRecordHasItUntilTheOfficeEdits()
    {
        // Before Edit rates the drawer is a record. Offering a peso here would be the screen answering for the office.
        var cut = RenderDrawer(Rate(FeeRateKey.ElecPerKwh, 0m), Rate(FeeRateKey.WaterPerCubicMeter, 0m));

        Assert.DoesNotContain("Electricity and water start at", cut.Markup);
        Assert.Empty(cut.FindAll(".cfg-rate-input"));
    }

    [Fact]
    public void PressingEditRatesOpensAnUnstatedMeteredRateAtOnePeso()
    {
        var cut = RenderDrawer(
            Rate(FeeRateKey.NpmDailyStall, 30m, stated: true),
            Rate(FeeRateKey.ElecPerKwh, 0m),
            Rate(FeeRateKey.WaterPerCubicMeter, 0m));

        PressEditRates(cut);

        var values = cut.FindAll(".cfg-rate-input input").Select(e => e.GetAttribute("value")).ToList();
        Assert.Equal(2, values.Count(v => v == "1"));
        // And the field says what it is doing, in one line.
        Assert.Contains("Electricity and water start at ₱1.00", cut.Markup);
    }

    [Fact]
    public void AMeteredRateTheOfficeHasAlreadyStatedIsNotOverwrittenByTheSuggestion()
    {
        var cut = RenderDrawer(
            Rate(FeeRateKey.ElecPerKwh, 12.5m, stated: true),
            Rate(FeeRateKey.WaterPerCubicMeter, 0m));

        PressEditRates(cut);

        var values = cut.FindAll(".cfg-rate-input input").Select(e => e.GetAttribute("value")).ToList();
        Assert.Contains("12.5", values);
        Assert.Contains("1", values);
    }

    [Fact]
    public void TheSuggestionIsNotOfferedForAFeeThatIsNotMetered()
    {
        // A daily stall fee left unstated already has an answer, and it is not a peso.
        var cut = RenderDrawer(Rate(FeeRateKey.NpmDailyStall, 0m));

        PressEditRates(cut);

        var values = cut.FindAll(".cfg-rate-input input").Select(e => e.GetAttribute("value")).ToList();
        Assert.DoesNotContain("1", values);
        Assert.DoesNotContain("Electricity and water start at", cut.Markup);
    }

    [Fact]
    public void NothingIsSentToTheOfficeRecordMerelyByOpeningTheForm()
    {
        // The figure is an offer on a form. It becomes a rate on Save and not before, which is the whole reason the drawer
        // reads as a record until the office says it is editing.
        var cut = RenderDrawer(Rate(FeeRateKey.ElecPerKwh, 0m), Rate(FeeRateKey.WaterPerCubicMeter, 0m));

        PressEditRates(cut);

        _api.Verify(a => a.SetFacilityRateAsync(It.IsAny<FacilityCode>(), It.IsAny<FeeRateKey>(), It.IsAny<decimal>()),
            Times.Never);
    }
}
