using System.Reflection;
using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BbqFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.BBQ;
using CustomFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.CustomFacility;
using IceFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.ICEPLANT;
using NccFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.NCC;
using NpmFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.NPM;
using SlaughterFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.SH;
using TccFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.TCC;
using TpmFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.TPM;
using TrmFacilityPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.TRM;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class OperationsFacilitiesHubTests : TestContext
{
    public OperationsFacilitiesHubTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    [Fact]
    public void OperationsRoute_UsesTheExistingOfficeRoles_AndClaimsNoFacilityDetailRoute()
    {
        var routes = typeof(Operations).GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToArray();
        var authorize = Assert.Single(typeof(Operations)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        var existingFacilityAuthorize = Assert.Single(typeof(NpmFacilityPage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(new[] { "/operations" }, routes);
        Assert.Equal("SuperAdmin,Admin", authorize.Roles);
        Assert.Equal(existingFacilityAuthorize.Roles, authorize.Roles);
        Assert.Null(authorize.Policy);
        Assert.Null(authorize.AuthenticationSchemes);
    }

    [Fact]
    public void RendersOnlyTenantSummaryFacilities_WithTenantNamesAndExistingSlugs()
    {
        var summaries = new[]
        {
            Summary(FacilityCode.NPM, "Madrid Public Market", "MPM"),
            Summary(FacilityCode.Custom1, "Riverside Market", "RVM"),
        };
        var api = FacilityApi(summaries);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[data-facility-code]").Count);
            Assert.Contains("Madrid Public Market", cut.Find("[data-facility-code='NPM']").TextContent);
            Assert.Contains("Riverside Market", cut.Find("[data-facility-code='Custom1']").TextContent);
            Assert.DoesNotContain("New Public Market", cut.Markup);
            Assert.DoesNotContain("Tampak Commercial Center", cut.Markup);
            AssertFacilityLink(cut, FacilityCode.NPM, "/facility/npm");
            AssertFacilityLink(cut, FacilityCode.Custom1, "/facility/rvm");
        }, Timeout);

        api.Verify(client => client.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void RepresentativeFacilityArchetypes_LinkToTheirExistingRouteOwners()
    {
        var summaries = new[]
        {
            Summary(FacilityCode.NPM, "Tenant Daily Market", "TDM"),
            Summary(FacilityCode.TCC, "Tenant Rental Center", "TRC"),
            Summary(FacilityCode.NCC, "Tenant Commercial Center", "TNC"),
            Summary(FacilityCode.BBQ, "Tenant Barbecue Stand", "TBS"),
            Summary(FacilityCode.ICE, "Tenant Iceplant", "TIP"),
            Summary(FacilityCode.SLH, "Tenant Slaughter Facility", "TSF"),
            Summary(FacilityCode.TRM, "Tenant Transport Terminal", "TTT"),
            Summary(FacilityCode.TPM, "Tenant Weekly Market", "TWM"),
            Summary(FacilityCode.Custom1, "Tenant Custom Rental", "TCR"),
            Summary(FacilityCode.Custom2, "Tenant Custom Market Two", "TC2"),
            Summary(FacilityCode.Custom3, "Tenant Custom Market Three", "TC3"),
            Summary(FacilityCode.Custom4, "Tenant Custom Market Four", "TC4"),
            Summary(FacilityCode.Custom5, "Tenant Custom Market Five", "TC5"),
        };
        Services.AddSingleton(FacilityApi(summaries).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            AssertFacilityLink(cut, FacilityCode.NPM, "/facility/npm");
            AssertFacilityLink(cut, FacilityCode.TCC, "/facility/tcc");
            AssertFacilityLink(cut, FacilityCode.NCC, "/facility/ncc");
            AssertFacilityLink(cut, FacilityCode.BBQ, "/facility/bbq");
            AssertFacilityLink(cut, FacilityCode.ICE, "/facility/ice");
            AssertFacilityLink(cut, FacilityCode.SLH, "/facility/slh");
            // Transportation is a Cash Ticket workspace (IA-050); the legacy trip pages stay on the TRM facility route below.
            AssertFacilityLink(cut, FacilityCode.TRM, "/operations/transportation");
            AssertFacilityLink(cut, FacilityCode.TPM, "/facility/tpm");
            AssertFacilityLink(cut, FacilityCode.Custom1, "/facility/tcr");
            AssertFacilityLink(cut, FacilityCode.Custom2, "/facility/tc2");
            AssertFacilityLink(cut, FacilityCode.Custom3, "/facility/tc3");
            AssertFacilityLink(cut, FacilityCode.Custom4, "/facility/tc4");
            AssertFacilityLink(cut, FacilityCode.Custom5, "/facility/tc5");
        }, Timeout);

        Assert.Same(typeof(NpmFacilityPage), RouteOwner("/facility/npm"));
        Assert.Same(typeof(TccFacilityPage), RouteOwner("/facility/tcc"));
        Assert.Same(typeof(NccFacilityPage), RouteOwner("/facility/ncc"));
        Assert.Same(typeof(BbqFacilityPage), RouteOwner("/facility/bbq"));
        Assert.Same(typeof(IceFacilityPage), RouteOwner("/facility/ice"));
        Assert.Same(typeof(SlaughterFacilityPage), RouteOwner("/facility/slh"));
        Assert.Same(typeof(TrmFacilityPage), RouteOwner("/facility/trm"));
        Assert.Same(typeof(TpmFacilityPage), RouteOwner("/facility/tpm"));

        var customRoute = Assert.Single(typeof(CustomFacilityPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());
        Assert.Equal("/facility/{Slug}", customRoute.Template);
    }

    [Fact]
    public void UndefinedFacilityCode_NeverGetsARoute()
    {
        var unsupportedCode = (FacilityCode)106;
        Assert.False(Enum.IsDefined(unsupportedCode));

        var summaries = new[]
        {
            Summary(FacilityCode.NPM, "Tenant Daily Market", "TDM"),
            Summary(FacilityCode.Custom5, "Tenant Custom Market Five", "TC5"),
            Summary(unsupportedCode, "Unmapped Tenant Facility", "UTF"),
        };
        Services.AddSingleton(FacilityApi(summaries).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            AssertFacilityLink(cut, FacilityCode.NPM, "/facility/npm");
            AssertFacilityLink(cut, FacilityCode.Custom5, "/facility/tc5");
            Assert.Empty(cut.FindAll("[data-facility-code='106']"));
            Assert.DoesNotContain(cut.FindAll("a"), link => link.GetAttribute("href") == "/facility/utf");
        }, Timeout);
    }

    [Fact]
    public void EmptyConfiguredFacilityResponse_DoesNotInventAnActiveFacilityList()
    {
        var api = FacilityApi(Array.Empty<FacilitySidebarSummaryDto>());
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No configured facilities are available.", cut.Find("[role='status']").TextContent);
            Assert.Empty(cut.FindAll("[data-facility-code]"));
            Assert.DoesNotContain(cut.FindAll("a"), link => link.GetAttribute("href")?.StartsWith("/facility/") == true);
        }, Timeout);
    }

    [Fact]
    public void FailedFacilitySource_ShowsRetryWithoutUsingTheFallbackFacilityCodes()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.SetupSequence(client => client.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("offline"))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                new[] { Summary(FacilityCode.Custom1, "Tenant Custom Facility", "TCF") }));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Configured facilities couldn't be loaded.", cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("[data-facility-code]"));

            // Facility-backed lines are unknown, not absent, while the office's record can't be read.
            Assert.Contains("Couldn't be loaded", Row(cut, "Tabo").TextContent);
            Assert.DoesNotContain("Not configured for this office", cut.Markup);
            Assert.Contains("Stall-rental facility lines couldn't be loaded.", cut.Markup);

            // Lines that never depended on the facility record keep their workspaces.
            Assert.Equal("/operations/market-fees", LinkOf(cut, "Market Fees"));
        }, Timeout);

        cut.Find("[role='alert'] button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[role='alert']"));
            AssertFacilityLink(cut, FacilityCode.Custom1, "/facility/tcf");
            Assert.Contains("Tenant Custom Facility", cut.Find("[data-facility-code='Custom1']").TextContent);
        }, Timeout);
    }

    [Fact]
    public void IncomeGroups_ShowConfirmedCantilanInstruments_AsText()
    {
        Services.AddSingleton(FacilityApi(new[] { Summary(FacilityCode.TPM, "Tabo-an Public Market", "TPM") }).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            // IA-045: Tabo uses Official Receipt.
            var tabo = Row(cut, "Tabo");
            Assert.Contains("Official Receipt", tabo.TextContent);
            Assert.DoesNotContain("Cash Ticket", tabo.TextContent);

            // IA-046: whole payment uses OR, a daily transaction uses CT.
            var vegetable = Row(cut, "Vegetable | Fruits");
            Assert.Contains("Official Receipt", vegetable.TextContent);
            Assert.Contains("Cash Ticket", vegetable.TextContent);
            Assert.Contains("Whole payment: Official Receipt", vegetable.TextContent);
            Assert.Contains("Daily transaction: Cash Ticket", vegetable.TextContent);

            Assert.Contains("Cash Ticket", Row(cut, "Market Fees").TextContent);
            Assert.Contains("Official Receipt", Row(cut, "Kanmanggay").TextContent);
            Assert.Contains("Official Receipt", Row(cut, "Transfer Large Cattle").TextContent); // IA-049
            Assert.DoesNotContain("Cash Ticket", Row(cut, "Transfer Large Cattle").TextContent);
        }, Timeout);
    }

    [Fact]
    public void RowsWithoutAWorkspace_AreNotLinks_AndLinkedRowsKeepTheirRoutes()
    {
        Services.AddSingleton(FacilityApi(new[] { Summary(FacilityCode.TPM, "Tabo-an Public Market", "TPM") }).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(Row(cut, "Arrears").QuerySelectorAll("a"));
            Assert.Contains("No workspace yet", Row(cut, "Arrears").TextContent);
            Assert.DoesNotContain("None in StallTrack", cut.Markup);

            Assert.Equal("/operations/transfer-large-cattle", LinkOf(cut, "Transfer Large Cattle"));
            Assert.Equal("/operations/vegetable-fruit", LinkOf(cut, "Vegetable | Fruits"));
            Assert.Equal("/operations/kanmanggay", LinkOf(cut, "Kanmanggay"));
            Assert.Equal("/operations/fiesta-araw", LinkOf(cut, "Fiesta | Araw"));
            Assert.Equal("/operations/fines", LinkOf(cut, "Fines"));
            Assert.Equal("/operations/market-fees", LinkOf(cut, "Market Fees"));
            Assert.Equal("/operations/ecf", LinkOf(cut, "Electricity Consumption Fees"));
            Assert.Equal("ECF", Row(cut, "Electricity Consumption Fees").QuerySelector(":scope > a .ops-row-code")?.TextContent);
            Assert.Equal("WCF", Row(cut, "Water Consumption Fees").QuerySelector(":scope > a .ops-row-code")?.TextContent);
            Assert.Equal("/operations/water-consumption-fees", LinkOf(cut, "Water Consumption Fees"));
            Assert.Equal("/operations/landing-berthing", LinkOf(cut, "Landing | Berthing"));
            Assert.Equal("/operations/fish-meat-vendor-fees", LinkOf(cut, "Fish | Meat Vendor Fees"));
            Assert.Equal("/operations/weight-and-measure", LinkOf(cut, "Weight & Measure | Registration"));
            Assert.Equal("/facility/tpm", LinkOf(cut, "Tabo"));

            // The detail card offers only existing related pages.
            var ecfLinks = Row(cut, "Electricity Consumption Fees").QuerySelectorAll(".ops-card a").Select(a => a.GetAttribute("href"));
            Assert.Equal(new[] { "/operations/ecf", "/operations/ecf/accounts", "/operations/ecf/report" }, ecfLinks);
        }, Timeout);
    }

    [Fact]
    public void Page_LeavesTheMainLandmarkToTheLayout_AndHeadsEachGroup()
    {
        Services.AddSingleton(FacilityApi(Array.Empty<FacilitySidebarSummaryDto>()).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            Assert.Equal("Operations", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            var groups = cut.FindAll("h2").Select(h => h.TextContent.Trim()).ToArray();
            Assert.Contains(groups, g => g.StartsWith("Income from Market", StringComparison.Ordinal));
            Assert.Contains(groups, g => g.StartsWith("Rent Income — Stall Rental", StringComparison.Ordinal));
            Assert.Contains(groups, g => g.StartsWith("Space Rental", StringComparison.Ordinal));

            // No authoritative content (no slaughterhouse or office-defined facility): no empty "Other operations" shell.
            Assert.DoesNotContain(groups, g => g.StartsWith("Other operations", StringComparison.Ordinal));
        }, Timeout);
    }

    [Fact]
    public void ConfiguredFacilityLine_NotInTheOfficeRecord_SaysNotConfigured()
    {
        Services.AddSingleton(FacilityApi(new[] { Summary(FacilityCode.NPM, "Tenant Daily Market", "TDM") }).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(Row(cut, "Tabo").QuerySelectorAll(":scope > a"));
            Assert.Contains("Not configured for this office", Row(cut, "Tabo").TextContent);
        }, Timeout);
    }

    [Fact]
    public void OtherOperations_AppearsOnlyWithAConfiguredSlaughterhouseOrCustomFacility()
    {
        Services.AddSingleton(FacilityApi(new[] { Summary(FacilityCode.SLH, "Tenant Slaughter Facility", "TSF") }).Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll("h2"), h => h.TextContent.Trim() == "Other operations");
            AssertFacilityLink(cut, FacilityCode.SLH, "/facility/slh");
        }, Timeout);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>The directory row whose visible text begins with the operation's name.</summary>
    private static AngleSharp.Dom.IElement Row(IRenderedComponent<Operations> cut, string name)
        => Assert.Single(cut.FindAll("li"), item => item.TextContent.TrimStart().StartsWith(name, StringComparison.Ordinal));

    /// <summary>The row's own link (the detail card may repeat it and add related pages).</summary>
    private static string? LinkOf(IRenderedComponent<Operations> cut, string name)
        => Assert.Single(Row(cut, name).QuerySelectorAll(":scope > a")).GetAttribute("href");

    private static FacilitySidebarSummaryDto Summary(FacilityCode code, string name, string shortName)
        => new(code, name, shortName, UnpaidCount: 0);

    private static Mock<IFacilitiesApiClient> FacilityApi(IEnumerable<FacilitySidebarSummaryDto> summaries)
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(client => client.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(summaries.ToArray()));
        return api;
    }

    private static void AssertFacilityLink(IRenderedComponent<Operations> cut, FacilityCode code, string expectedHref)
        => Assert.Equal(expectedHref, cut.Find($"[data-facility-code='{code}'] a").GetAttribute("href"));

    private static Type RouteOwner(string template)
    {
        var owners = typeof(Operations).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>()
                .Any(route => string.Equals(route.Template, template, StringComparison.Ordinal)))
            .ToArray();

        return Assert.Single(owners);
    }
}
