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
        _api.Setup(a => a.AddNpmCustomSectionAsync(It.IsAny<string>())).ReturnsAsync(Result<bool>.Success(true));
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
        // to ask for something it cannot have, and the two must agree.
        var cut = RenderDrawer(new NpmCustomSectionDto("Sari-sari Area", 0), new NpmCustomSectionDto("Bakery Area", 4));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".cfg-sec-remove")));
        Assert.Contains("In use", cut.Markup);
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

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[placeholder='e.g. Sari-sari Area']")));
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("  sari-sari area  ");
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("already has a section called", cut.Markup));
        _api.Verify(a => a.AddNpmCustomSectionAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ACanonicalAreasOwnNameIsRefusedToo()
    {
        // "Fish Area" is what the platform calls an area the office has not renamed, and a second section by that name
        // would read alike and file apart.
        var cut = RenderDrawer();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[placeholder='e.g. Sari-sari Area']")));
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("Fish Area");
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("already has a section called", cut.Markup));
        _api.Verify(a => a.AddNpmCustomSectionAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ANewSectionIsRegisteredForTheWholeOffice()
    {
        var cut = RenderDrawer();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[placeholder='e.g. Sari-sari Area']")));
        cut.Find("input[placeholder='e.g. Sari-sari Area']").Input("  Sari-sari Area  ");
        cut.FindAll(".cfg-sec-add")[0].Click();

        // Trimmed, and stated back to the office in its own words.
        cut.WaitForAssertion(() => _api.Verify(a => a.AddNpmCustomSectionAsync("Sari-sari Area"), Times.Once));
        cut.WaitForAssertion(() => Assert.Contains("is now one of your market's sections", cut.Markup));
    }

    [Fact]
    public void AnEmptyNameAsksNothingOfTheServer()
    {
        var cut = RenderDrawer();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".cfg-sec-add")));
        cut.FindAll(".cfg-sec-add")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("Enter the section's name.", cut.Markup));
        _api.Verify(a => a.AddNpmCustomSectionAsync(It.IsAny<string>()), Times.Never);
    }
}
