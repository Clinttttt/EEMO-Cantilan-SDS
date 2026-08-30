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

using FacilityConfiguration = EEMOCantilanSDS.Client.Components.Pages.Menus.FacilityConfiguration;

/// <summary>
/// Laying out the market's own sections from Facility Configuration.
///
/// <para>
/// A section of an office's own could only come into being as a side effect of recording a stall in it, so an office
/// could not lay out its market before there was anyone to put in it. It belongs in Facility Configuration, beside the
/// three canonical areas it already renames there, and these hold that block to the rules the server enforces: a
/// section nothing is filed under may go, one with stalls in it may not, and a name the market already uses is refused
/// before a request is made.
/// </para>
/// </summary>
public class FacilityConfigurationSectionsTests : TestContext
{
    private const string Npm = "NPM";

    private Mock<IFacilitiesApiClient> _api = new();

    /// <summary>Opens the market's own configuration drawer, with the sections the office has registered.</summary>
    private IRenderedComponent<FacilityConfiguration> RenderDrawer(params NpmCustomSectionDto[] sections)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api = new Mock<IFacilitiesApiClient>();
        _api.Setup(a => a.GetFacilityConfigurationAsync())
            .ReturnsAsync(Result<FacilityConfigurationDto>.Success(new FacilityConfigurationDto(
                new List<ConfiguredFacilityDto>
                {
                    new(Npm, "New Public Market", "NPM", null, "Daily stall rental",
                        true, 0, new List<ConfiguredRateDto>()),
                },
                new List<AvailableFacilityDto>())));
        _api.Setup(a => a.GetNpmCustomSectionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<NpmCustomSectionDto>>.Success(sections.ToList()));
        _api.Setup(a => a.AddNpmCustomSectionAsync(It.IsAny<string>(), It.IsAny<decimal?>())).ReturnsAsync(Result<bool>.Success(true));
        _api.Setup(a => a.SetNpmSectionRateAsync(It.IsAny<string>(), It.IsAny<decimal>())).ReturnsAsync(Result<bool>.Success(true));
        _api.Setup(a => a.SetNpmSectionClosedAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(Result<int>.Success(1));
        _api.Setup(a => a.RemoveNpmCustomSectionAsync(It.IsAny<string>())).ReturnsAsync(Result<bool>.Success(true));
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

        // The block lives in the market's own configuration drawer, which the office opens from its card.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".fac-card")));
        cut.Find(".fac-card").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-drawer")));

        return cut;
    }

    [Fact]
    public void TheMarketsOwnSectionsAreListedWithWhatIsFiledUnderThem()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0), new NpmCustomSectionDto("Bakery Area", 4));

        cut.WaitForAssertion(() => Assert.Contains("Sari-sari Area", cut.Markup));
        Assert.Contains("Bakery Area", cut.Markup);
        Assert.Contains("4 stalls", cut.Markup);
    }

    [Fact]
    public void OnlyASectionNothingIsFiledUnderOffersToBeRemoved()
    {
        // The server refuses to remove a section with stalls in it. Offering the control anyway would invite the office
        // to ask for something it cannot have, and the two must agree. The stall count on the row says why.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0), new NpmCustomSectionDto("Bakery Area", 4));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cfg-sec-remove")));
        Assert.Contains("4 stalls", cut.Markup);
        // Its fee can still be set: a section in use is priced like any other.
        Assert.Equal(2, cut.FindAll(".cfg-sec-set").Count);
    }

    [Fact]
    public void RemovingASectionAsksTheOfficesRecordToForgetIt()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cfg-sec-remove")));
        cut.Find(".cfg-sec-remove").Click();

        cut.WaitForAssertion(() =>
            _api.Verify(a => a.RemoveNpmCustomSectionAsync("Sari-sari Area"), Times.Once));
    }

    [Fact]
    public void ANameTheMarketAlreadyUsesIsRefusedBeforeAnythingIsAsked()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0));

        OpenAddForm(cut);
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("  sari-sari area  ");
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("already has a section called", cut.Markup));
        _api.Verify(a => a.AddNpmCustomSectionAsync(It.IsAny<string>(), It.IsAny<decimal?>()), Times.Never);
    }

    [Fact]
    public void ACanonicalAreasOwnNameIsRefusedToo()
    {
        // "Fish Area" is what the platform calls an area the office has not renamed, and a second section by that name
        // would read alike and file apart.
        var cut = RenderDrawer();

        OpenAddForm(cut);
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("Fish Area");
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("already has a section called", cut.Markup));
        _api.Verify(a => a.AddNpmCustomSectionAsync(It.IsAny<string>(), It.IsAny<decimal?>()), Times.Never);
    }

    [Fact]
    public void ANewSectionIsRegisteredForTheWholeOffice()
    {
        var cut = RenderDrawer();

        OpenAddForm(cut);
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("  Sari-sari Area  ");
        cut.FindAll(".cfg-sec-add")[0].Click();

        // Trimmed, and stated back to the office in its own words.
        cut.WaitForAssertion(() => _api.Verify(a => a.AddNpmCustomSectionAsync("Sari-sari Area", null), Times.Once));
        cut.WaitForAssertion(() => Assert.Contains("is now one of your market's sections", cut.Markup));
    }

    [Fact]
    public void AnEmptyNameAsksNothingOfTheServer()
    {
        var cut = RenderDrawer();

        OpenAddForm(cut);
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("Enter the section's name.", cut.Markup));
        _api.Verify(a => a.AddNpmCustomSectionAsync(It.IsAny<string>(), It.IsAny<decimal?>()), Times.Never);
    }

    [Fact]
    public void ASectionStatesTheFeeTheOfficeSetForIt_OrTheMarketsRate()
    {
        // A section left unpriced has its stalls billed the market's own rate, and the row says so rather than showing a
        // nought that would read as free.
        var cut = RenderDrawer(
            new NpmCustomSectionDto("Sari-sari Area", 0, 25m),
            new NpmCustomSectionDto("Bakery Area", 4, null));

        cut.WaitForAssertion(() => Assert.Contains("₱25 a day", cut.Markup));
        Assert.Contains("Market rate", cut.Markup);
    }

    [Fact]
    public void SavingAFeeTellsTheOfficeWhatItWillBeBilledAndFromWhen()
    {
        // The one thing an office must be told when it changes a fee: from when. The rows are effective-dated, so a rate
        // stated today leaves yesterday's unpaid day on the figure it was always owed at.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0, null));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cfg-sec-set")));
        cut.Find(".cfg-sec-set").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-rate-input input")));
        cut.Find(".cfg-rate-input input").Change("25");
        cut.Find(".cfg-sec-save").Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.SetNpmSectionRateAsync("Sari-sari Area", 25m), Times.Once));
        cut.WaitForAssertion(() => Assert.Contains("a day from today", cut.Markup));
    }

    [Fact]
    public void ASectionMayBePricedAsItIsCreated()
    {
        var cut = RenderDrawer();

        OpenAddForm(cut);
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("Sari-sari Area");
        cut.Find(".cfg-rate-input input").Change("25");
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.AddNpmCustomSectionAsync("Sari-sari Area", 25m), Times.Once));
    }

    [Fact]
    public void ASectionCreatedWithNoFeeIsAskedForWithNone()
    {
        // Not nought: nought is a withdrawn figure, and a section created without a fee is simply unpriced.
        var cut = RenderDrawer();

        OpenAddForm(cut);
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("Bakery Area");
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.AddNpmCustomSectionAsync("Bakery Area", null), Times.Once));
    }

    /// <summary>Opens the add form, which the drawer keeps shut until the office asks for it.</summary>
    private static void OpenAddForm(IRenderedComponent<FacilityConfiguration> cut)
    {
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-add-open")));
        cut.Find(".cfg-sec-add-open").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[placeholder='e.g. Sari-sari Area']")));
    }

    [Fact]
    public void TheAddFormIsAskedForRatherThanLeftOpen()
    {
        // A drawer that presents empty fields invites a clerk to fill them in when they came to read the office's record.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0, null));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-add-open")));
        Assert.Empty(cut.FindAll("input[placeholder='e.g. Sari-sari Area']"));

        OpenAddForm(cut);
        Assert.NotEmpty(cut.FindAll("input[placeholder='e.g. Sari-sari Area']"));
    }

    [Fact]
    public void ASectionStatesWhatIsFiledUnderItAndWhatItCosts_AndNothingElse()
    {
        // A section's metering default was removed on 2026-08-30: every new market stall already opens with both meters
        // ticked, and the default could only ever ADD one, so it could add nothing that was not there. Its only reachable
        // effect was to re-tick a meter the clerk had just unticked, if they then changed the section. The row states what
        // is filed under the section and what it costs, and the meters belong to the stall's own form.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 1, 25m));

        cut.WaitForAssertion(() => Assert.Contains("Sari-sari Area", cut.Markup));
        Assert.Contains("1 stall · ₱25 a day", cut.Markup);

        Assert.DoesNotContain("Electricity", cut.Markup);
        Assert.DoesNotContain("Water", cut.Markup);
        Assert.DoesNotContain("new stall starts with", cut.Markup);
        Assert.Empty(cut.FindAll(".cfg-sec-util input"));
    }

    [Fact]
    public void EditingASectionOffersItsFeeAndNoMeters()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0, 25m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-set")));
        cut.Find(".cfg-sec-set").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cfg-rate-input input")));
        Assert.Empty(cut.FindAll("input[type=checkbox]"));
    }

    [Fact]
    public void ClosingTheEditWithoutSavingAsksNothingOfTheServer()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0, 25m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-set")));
        cut.Find(".cfg-sec-set").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-rate-input input")));
        cut.Find(".cfg-rate-input input").Change("40");
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".cfg-rate-input input")));
        _api.Verify(a => a.SetNpmSectionRateAsync(It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public void SavingWithNothingChangedAsksNothingOfTheServer()
    {
        // An office that opens a section to read it and closes it again must not gain a rate row dated today: the history is
        // a record of decisions, not of visits to this drawer.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0, 25m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-set")));
        cut.Find(".cfg-sec-set").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-save")));
        cut.Find(".cfg-sec-save").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".cfg-sec-save")));
        _api.Verify(a => a.SetNpmSectionRateAsync(It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }

    // ── Closing a section, which closes the stalls in it ───────────────────────────────────────────

    [Fact]
    public void ClosingASectionIsOfferedOnlyInsideTheEdit_AndAsksBeforeItActs()
    {
        // The office chose for closing a section to close its stalls too, so this must never happen on one press. The first
        // press states what will happen and how many spaces it reaches; the act is a second, separate press.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 2, 25m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-set")));
        Assert.Empty(cut.FindAll(".cfg-sec-close"));            // nothing offered while the section is only stated

        cut.Find(".cfg-sec-set").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close")));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Close section").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close-warn")));
        Assert.Contains("also closes its 2 stalls", cut.Markup);
        Assert.Contains("stop being billed from today", cut.Markup);
        Assert.Contains("excuses the closed days", cut.Markup);

        // And nothing has been asked of the server yet.
        _api.Verify(a => a.SetNpmSectionClosedAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void TheWarningCanBeDeclined()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 1, 25m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-set")));
        cut.Find(".cfg-sec-set").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close")));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Close section").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close-warn")));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Keep open").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".cfg-sec-close-warn")));
        _api.Verify(a => a.SetNpmSectionClosedAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void ConfirmingClosesTheSectionAndSaysWhatItDid()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 1, 25m));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-set")));
        cut.Find(".cfg-sec-set").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close")));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Close section").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close-warn")));
        cut.FindAll("button").First(b => b.TextContent.Contains("Close section and")).Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.SetNpmSectionClosedAsync("Sari-sari Area", true), Times.Once));
        // The count comes from the server's answer, not from what this screen last read.
        cut.WaitForAssertion(() => Assert.Contains("1 stall closed with it", cut.Markup));
    }

    [Fact]
    public void AClosedSectionStatesSoAndOffersToReopen()
    {
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 1, 25m, IsClosed: true));

        cut.WaitForAssertion(() => Assert.Contains("Closed · 1 stall", cut.Markup));

        cut.Find(".cfg-sec-set").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-close")));

        // No second warning to reopen: returning a space and excusing its closed days takes nothing away.
        Assert.Empty(cut.FindAll(".cfg-sec-close-warn"));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Reopen section").Click();

        cut.WaitForAssertion(() => _api.Verify(a => a.SetNpmSectionClosedAsync("Sari-sari Area", false), Times.Once));
    }

    // ── The rates: a record until the office says it is editing one ───────────────────────────────

    /// <summary>Opens the drawer for a market that has the ordinance rates this office would actually hold.</summary>
    private IRenderedComponent<FacilityConfiguration> RenderDrawerWithRates(FacilityState? catalog = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _api = new Mock<IFacilitiesApiClient>();
        _api.Setup(a => a.GetFacilityConfigurationAsync())
            .ReturnsAsync(Result<FacilityConfigurationDto>.Success(new FacilityConfigurationDto(
                new List<ConfiguredFacilityDto>
                {
                    new(Npm, "New Public Market", "NPM", null, "Daily stall rental", true, 0, new List<ConfiguredRateDto>
                    {
                        new(nameof(FeeRateKey.NpmDailyStall), "Daily stall fee", 30m, true),
                        new(nameof(FeeRateKey.NpmDailyStallMeat), "Meat section", 0m, false),
                    }),
                },
                new List<AvailableFacilityDto>())));
        _api.Setup(a => a.GetNpmCustomSectionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<NpmCustomSectionDto>>.Success(new List<NpmCustomSectionDto>()));
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
        Services.AddSingleton(catalog ?? FacilityCatalogFixture.WithNoRecord());
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");

        var cut = RenderComponent<FacilityConfiguration>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".fac-card")));
        cut.Find(".fac-card").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-drawer")));
        return cut;
    }

    [Fact]
    public void RatesAreStatedNotOfferedForTyping()
    {
        // A rate is the ordinance's figure. Opening the drawer on a form of live number fields invited a stray keystroke
        // to become a rate change on Save, and read as a data-entry screen rather than a record of what the office charges.
        var cut = RenderDrawerWithRates();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-rate-value")));
        Assert.Empty(cut.FindAll(".cfg-rate-input input"));
        Assert.Contains("Edit rates", cut.Markup);
    }

    [Fact]
    public void EditingIsADeliberateAct()
    {
        var cut = RenderDrawerWithRates();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-rate-value")));
        cut.FindAll("button").First(b => b.TextContent.Contains("Edit rates")).Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-rate-input input")));
        Assert.Empty(cut.FindAll(".cfg-rate-value"));
        // And the one thing the office must know before changing a rate, said once.
        Assert.Contains("take effect today", cut.Markup);
    }

    [Fact]
    public void AnAreasRateSaysWhatAnUnstatedOneMeans_OnceForTheGroup()
    {
        // The reading did not disappear with the long labels: it moved to one line under the group it applies to.
        var cut = RenderDrawerWithRates();

        cut.WaitForAssertion(() => Assert.Contains("Daily fee by area", cut.Markup));
        Assert.Contains("billed the market's daily stall fee", cut.Markup);
        Assert.DoesNotContain("0 = market rate", cut.Markup);
    }

    [Fact]
    public void AnAreasRateIsNamedByTheOfficesOwnWordForThatArea()
    {
        // Madrid calls its meat area Karne. The label the API sends is the platform's fallback wording, and this screen is
        // where the office states its own — so the row it edits must be the row it recognises.
        var cut = RenderDrawerWithRates(FacilityCatalogFixture.NamingTheMarket(
            "Madrid Public Market", "MPM", "Gulayan", "Isda", "Karne"));

        cut.WaitForAssertion(() => Assert.Contains("Daily fee by area", cut.Markup));
        Assert.Contains("Karne", cut.Markup);
        Assert.DoesNotContain("Meat section", cut.Markup);
    }
}
