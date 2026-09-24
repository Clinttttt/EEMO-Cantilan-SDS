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
            Assert.Equal(2, cut.FindAll(".operations-row").Count);
            Assert.Contains("Madrid Public Market", cut.Markup);
            Assert.Contains("MPM", cut.Markup);
            Assert.Contains("Riverside Market", cut.Markup);
            Assert.DoesNotContain("New Public Market", cut.Markup);
            Assert.DoesNotContain("Tampak Commercial Center", cut.Markup);
            Assert.Equal("/facility/npm", cut.Find("[data-facility-code='NPM'] .operations-open").GetAttribute("href"));
            Assert.Equal("/facility/rvm", cut.Find("[data-facility-code='Custom1'] .operations-open").GetAttribute("href"));
        });

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
            AssertFacilityLink(cut, FacilityCode.TRM, "/facility/trm");
            AssertFacilityLink(cut, FacilityCode.TPM, "/facility/tpm");
            AssertFacilityLink(cut, FacilityCode.Custom1, "/facility/tcr");
            AssertFacilityLink(cut, FacilityCode.Custom2, "/facility/tc2");
            AssertFacilityLink(cut, FacilityCode.Custom3, "/facility/tc3");
            AssertFacilityLink(cut, FacilityCode.Custom4, "/facility/tc4");
            AssertFacilityLink(cut, FacilityCode.Custom5, "/facility/tc5");
        });

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
    public void UndefinedFacilityCode_IsInformationalOnlyAndNeverGetsARoute()
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
            var unsupportedRow = cut.Find("[data-facility-code='106']");
            Assert.Contains("Unmapped Tenant Facility", unsupportedRow.TextContent);
            Assert.Contains("UTF", unsupportedRow.TextContent);
            Assert.Contains("Unsupported code (106)", unsupportedRow.TextContent);
            Assert.Empty(unsupportedRow.QuerySelectorAll("a"));
            Assert.DoesNotContain("/facility/", unsupportedRow.OuterHtml);
            AssertFacilityLink(cut, FacilityCode.NPM, "/facility/npm");
            AssertFacilityLink(cut, FacilityCode.Custom5, "/facility/tc5");
        });
    }

    [Fact]
    public void EmptyConfiguredFacilityResponse_DoesNotInventAnActiveFacilityList()
    {
        var api = FacilityApi(Array.Empty<FacilitySidebarSummaryDto>());
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Operations>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No active facilities are available to open.", cut.Markup);
            Assert.Empty(cut.FindAll(".operations-row"));
        });
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
            Assert.Contains("The facility list is temporarily unavailable.", cut.Markup);
            Assert.Empty(cut.FindAll(".operations-row"));
        });

        cut.Find(".operations-error button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Tenant Custom Facility", cut.Markup);
            Assert.Single(cut.FindAll(".operations-row"));
        });
    }

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
        => Assert.Equal(expectedHref, cut.Find($"[data-facility-code='{code}'] .operations-open").GetAttribute("href"));

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
