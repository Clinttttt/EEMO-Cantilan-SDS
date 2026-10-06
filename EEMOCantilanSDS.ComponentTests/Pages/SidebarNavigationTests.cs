using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Client.Components.Pages.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class SidebarNavigationTests : TestContext
{
    private readonly Mock<IFacilitiesApiClient> _facilitiesApi = new();
    private readonly TestAuthorizationContext _authorization;

    public SidebarNavigationTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();

        _facilitiesApi
            .Setup(api => api.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
            [
                new(FacilityCode.NPM, "New Public Market", "NPM", 5),
                new(FacilityCode.TCC, "Tampak Commercial Center", "TCC", 2),
                new(FacilityCode.NCC, "New Commercial Center", "NCC", 1),
                new(FacilityCode.BBQ, "Barbecue Stand", "BBQ", 0),
                new(FacilityCode.ICE, "Iceplant", "ICE", 0),
                new(FacilityCode.SLH, "Slaughterhouse", "SLH", 3),
                new(FacilityCode.TRM, "Transport Terminal", "TRM", 0),
                new(FacilityCode.TPM, "Tabo-an Public Market", "TPM", 1),
                new(FacilityCode.Custom1, "Madrid Custom Facility", "MCF", 0),
            ]));
        Services.AddSingleton(_facilitiesApi.Object);

        var onlinePaymentsApi = new Mock<IOnlinePaymentsApiClient>();
        onlinePaymentsApi
            .Setup(api => api.GetAwaitingOrAsync())
            .ReturnsAsync(Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>.Success(
                Array.Empty<OnlinePaymentAwaitingOrDto>()));
        Services.AddSingleton(onlinePaymentsApi.Object);

        _authorization = this.AddTestAuthorization();
        _authorization.SetAuthorized("head").SetRoles("SuperAdmin");
    }

    [Fact]
    public void HeadSeesTheCuratedDestinationsInOrder()
    {
        var cut = RenderComponent<Sidebar>();
        var links = cut.FindAll(".sidebar-nav a.nav-item");

        Assert.Equal(
            [
                "/overview",
                "/operations",
                "/collections/activity",
                "/online-payments",
                "/vendors",
                "/monitoring/follow-up",
                "/remittances",
                "/reports",
                "/collectors",
                "/audit-trail",
                "/settings",
            ],
            links.Select(link => link.GetAttribute("href") ?? string.Empty).ToArray());

        Assert.Equal(
            [
                "Overview",
                "Operations",
                "Collection Activity",
                "Online Payments",
                "Spaces & Occupants",
                "Monitoring",
                "Remittance & Liquidation",
                "Reports",
                "Collectors",
                "Audit Trail",
                "Settings",
            ],
            links.Select(link => link.QuerySelector("span:not(.nav-badge)")?.TextContent.Trim() ?? string.Empty).ToArray());

        Assert.Equal("Main", Assert.Single(cut.FindAll(".sidebar-nav .nav-group-label")).TextContent.Trim());
    }

    [Fact]
    public void FacilityRowsAndSecondaryDestinationsAreAbsent_AndFacilitySummariesAreNotLoaded()
    {
        var cut = RenderComponent<Sidebar>();
        var links = cut.FindAll(".sidebar-nav a.nav-item");

        Assert.DoesNotContain(links, link => link.GetAttribute("href")?.StartsWith("/facility/", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(links, link => link.GetAttribute("href") == "/export");
        Assert.DoesNotContain("Collection Manager", cut.Markup);
        Assert.DoesNotContain("Follow-up History", cut.Markup);

        foreach (var oldFacilityName in new[]
        {
            "New Public Market",
            "Tampak Commercial Center",
            "New Commercial Center",
            "Barbecue Stand",
            "Iceplant",
            "Slaughterhouse",
            "Transport Terminal",
            "Tabo-an Public Market",
            "Madrid Custom Facility",
        })
        {
            Assert.DoesNotContain(oldFacilityName, cut.Markup);
        }

        _facilitiesApi.Verify(
            api => api.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public void AdminKeepsCollectorsAndAuditTrailVisibleAsLockedItems()
    {
        _authorization.SetAuthorized("admin").SetRoles("Admin");

        var cut = RenderComponent<Sidebar>();
        var lockedItems = cut.FindAll(".sidebar-nav .nav-item-locked");

        Assert.Equal(
            ["Collectors", "Audit Trail"],
            lockedItems.Select(item => item.QuerySelector("span")?.TextContent.Trim() ?? string.Empty).ToArray());
        Assert.DoesNotContain(cut.FindAll(".sidebar-nav a.nav-item"), link => link.GetAttribute("href") == "/collectors");
        Assert.DoesNotContain(cut.FindAll(".sidebar-nav a.nav-item"), link => link.GetAttribute("href") == "/audit-trail");
    }

    [Theory]
    [InlineData("/overview", "/overview")]
    [InlineData("/menu", "/overview")]
    [InlineData("/", "/overview")]
    [InlineData("/operations", "/operations")]
    [InlineData("/facility/npm", "/operations")]
    [InlineData("/collections/activity", "/collections/activity")]
    [InlineData("/transactions", "/collections/activity")]
    [InlineData("/online-payments", "/online-payments")]
    [InlineData("/vendors", "/vendors")]
    [InlineData("/profile/NPM/Space-1", "/vendors")]
    [InlineData("/payor/profile", "/vendors")]
    [InlineData("/monitoring/follow-up", "/monitoring/follow-up")]
    [InlineData("/reports/follow-up", "/monitoring/follow-up")]
    [InlineData("/reports/follow-up/history", "/monitoring/follow-up")]
    [InlineData("/remittances", "/remittances")]
    [InlineData("/accountable-forms", "/remittances")]
    [InlineData("/reports", "/reports")]
    [InlineData("/reports/financial-summary", "/reports")]
    [InlineData("/collectors", "/collectors")]
    [InlineData("/audit-trail", "/audit-trail")]
    [InlineData("/settings", "/settings")]
    [InlineData("/settings/facilities", "/settings")]
    [InlineData("/admin", "/settings")]
    public void CurrentAndLegacyPathsActivateTheirOwningDestination(string route, string destination)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);

        var cut = RenderComponent<Sidebar>();
        var activeLink = Assert.Single(cut.FindAll(".sidebar-nav a.nav-item.active"));

        Assert.Equal(destination, activeLink.GetAttribute("href") ?? string.Empty);
    }

    [Theory]
    [InlineData("/menu")]
    [InlineData("/transactions")]
    [InlineData("/reports/follow-up")]
    [InlineData("/admin")]
    [InlineData("/export")]
    [InlineData("/facility/npm")]
    [InlineData("/facility/{Slug}")]
    public void LegacyRoutesRemainRegistered(string route)
    {
        var matchingRouteOwners = typeof(Sidebar).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>()
                .Any(attribute => string.Equals(attribute.Template, route, StringComparison.Ordinal)))
            .ToArray();

        Assert.Single(matchingRouteOwners);
    }
}
