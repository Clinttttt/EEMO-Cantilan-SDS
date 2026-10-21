using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Operations directory renders from the office's own configuration. An office that chose its operations sees only those,
/// under its own names; an office that never adopted configuration (Cantilan) is unchanged. A facility-backed line needs the
/// facility. Listing is presentation: nothing here is, or implies, financial authorization.
/// </summary>
public sealed class OperationsTenantConfigurationTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public OperationsTenantConfigurationTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    internal static Mock<IOperationSettingsApiClient> SettingsApi(
        bool adopted, IEnumerable<string> listed, IReadOnlyDictionary<string, string>? labels = null)
    {
        var on = listed.ToHashSet(StringComparer.Ordinal);
        var items = TenantOperationCatalog.All.Select(e =>
        {
            string? label = null;
            labels?.TryGetValue(e.Code, out label);
            return new TenantOperationSettingDto(
                e.Code, e.DefaultLabel, e.GroupKey, label,
                adopted ? label ?? e.DefaultLabel : e.LegacyDirectoryLabel,
                adopted ? on.Contains(e.Code) : true, adopted ? 1 : 0, adopted ? "Onboarding" : "Legacy", HasRecordedActivity: false);
        }).ToList();
        var api = new Mock<IOperationSettingsApiClient>();
        api.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Success(new TenantOperationSettingsDto(adopted, items)));
        return api;
    }

    private static Mock<IFacilitiesApiClient> Facilities(params FacilityCode[] codes)
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(c => c.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                codes.Select(c => new FacilitySidebarSummaryDto(c, $"Facility {c}", c.ToString(), UnpaidCount: 0)).ToArray()));
        return api;
    }

    private static string[] Lines(IRenderedComponent<Operations> cut) =>
        cut.FindAll(".ops-row-name").Select(e => e.TextContent.Trim()).ToArray();

    private static string[] Groups(IRenderedComponent<Operations> cut) =>
        cut.FindAll("h2").Select(h => h.TextContent.Trim()).ToArray();

    [Fact]
    public void ANewMunicipality_SeesOnlyTheOperationsItChose_UnderItsOwnNames()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object);
        Services.AddSingleton(SettingsApi(adopted: true,
            listed: ["MARKET_FEES", "KANMANGGAY_SPACE_RENTAL"],
            labels: new Dictionary<string, string> { ["KANMANGGAY_SPACE_RENTAL"] = "Municipal Plaza Rental" }).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var lines = string.Join("|", Lines(cut));
            Assert.Contains("Market Fees", lines);
            Assert.Contains("Municipal Plaza Rental", lines); // the office's own wording
            Assert.DoesNotContain("Kanmanggay", lines);       // Cantilan's name is not imposed
            // Operations it never chose are not presented as active collection lines.
            Assert.DoesNotContain("Landing", lines);
            Assert.DoesNotContain("Electricity", lines);
            Assert.DoesNotContain("Transportation", lines);
            Assert.DoesNotContain("Fish", lines);
        }, Timeout);
    }

    [Fact]
    public void FacilityBackedLines_NeedTheFacility_ForAnOfficeThatChoseItsOperations()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object); // no Tabo, no Ice Plant, no Terminal
        Services.AddSingleton(SettingsApi(adopted: true, listed: ["MARKET_FEES"]).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var lines = string.Join("|", Lines(cut));
            Assert.DoesNotContain("Tabo", lines);
            Assert.DoesNotContain("Ice Plant", lines);
            Assert.DoesNotContain("Terminal", lines);
            Assert.DoesNotContain(Groups(cut), g => g.StartsWith("Income From Terminal", StringComparison.Ordinal));
        }, Timeout);
    }

    [Fact]
    public void WithItsFacility_TheTerminalAndTaboLinesAppear()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM, FacilityCode.TRM, FacilityCode.TPM).Object);
        Services.AddSingleton(SettingsApi(adopted: true, listed: ["MARKET_FEES"]).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var lines = string.Join("|", Lines(cut));
            Assert.Contains("Tabo", lines);
            Assert.Contains("Terminal", lines);
            Assert.Contains(Groups(cut), g => g.StartsWith("Income From Terminal", StringComparison.Ordinal));
        }, Timeout);
    }

    [Fact]
    public void GroupCountsAndEmptyGroups_FollowWhatIsActuallyShown()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object);
        Services.AddSingleton(SettingsApi(adopted: true, listed: ["MARKET_FEES", "WEIGHT_AND_MEASURE"]).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var market = Groups(cut).Single(g => g.StartsWith("Income from Market", StringComparison.Ordinal));
            Assert.Contains("2 lines", market);
            // Nothing in Space Rental was chosen, so there is no empty shell announcing it.
            Assert.DoesNotContain(Groups(cut), g => g.StartsWith("Space Rental", StringComparison.Ordinal));
        }, Timeout);
    }

    [Fact]
    public void AnOfficeThatNeverAdopted_IsUnchanged_EveryLineUnderItsEstablishedName()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object);
        Services.AddSingleton(SettingsApi(adopted: false, listed: []).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var lines = string.Join("|", Lines(cut));
            Assert.Contains("Kanmanggay", lines);
            Assert.Contains("Fiesta | Araw", lines);
            Assert.Contains("Landing | Berthing", lines);
            Assert.Contains("Vegetable | Fruits", lines);
            Assert.Contains(Groups(cut), g => g.StartsWith("Income From Terminal", StringComparison.Ordinal));
            Assert.Contains("10 lines", Groups(cut).Single(g => g.StartsWith("Income from Market", StringComparison.Ordinal)));
        }, Timeout);
    }

    [Fact]
    public void WhenTheConfigurationCannotBeRead_NoIndependentLineIsGuessedAt()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object);
        var failing = new Mock<IOperationSettingsApiClient>();
        failing.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Failure("down", 503));
        Services.AddSingleton(failing.Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("operation list couldn't be loaded", cut.Markup);
            Assert.DoesNotContain("Market Fees", string.Join("|", Lines(cut)));
        }, Timeout);
    }

    [Fact]
    public void ARuleAcceptedOnlyForCantilan_IsMarkedOnItsDirectoryLine_ForAnotherMunicipality()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object);
        var model = SettingsApi(adopted: true, listed: ["KANMANGGAY_SPACE_RENTAL", "FIESTA_ARAW_LOT_RENTAL"]).Object.GetAsync().Result.Value!;
        var withNotice = model with
        {
            Items = model.Items.Select(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL" ? i with { PolicyNotice = "confirm" } : i).ToList(),
        };
        var api = new Mock<IOperationSettingsApiClient>();
        api.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Success(withNotice));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("li").Where(li => li.TextContent.Contains("Policy confirmation required")).ToList();
            var row = Assert.Single(rows);
            Assert.Contains("Municipal Space Rental", row.TextContent);
        }, Timeout);
    }

    [Fact]
    public void ALocalName_ChangesTheLabelOnly_NotTheRouteOrTheReceiptInstrument()
    {
        Services.AddSingleton(Facilities(FacilityCode.NPM).Object);
        Services.AddSingleton(SettingsApi(adopted: true, listed: ["KANMANGGAY_SPACE_RENTAL"],
            labels: new Dictionary<string, string> { ["KANMANGGAY_SPACE_RENTAL"] = "OR Plaza Rental" }).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("li"), li => li.TextContent.TrimStart().StartsWith("OR Plaza Rental", StringComparison.Ordinal));
            Assert.Equal("/operations/kanmanggay", Assert.Single(row.QuerySelectorAll(":scope > a")).GetAttribute("href"));
            // The instrument is the operation's own (Official Receipt), whatever the label says.
            Assert.Contains("Official Receipt", row.TextContent);
        }, Timeout);
    }
}
