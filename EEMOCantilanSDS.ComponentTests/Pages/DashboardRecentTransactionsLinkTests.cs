using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class DashboardRecentTransactionsLinkTests : TestContext
{
    private readonly Mock<IOfficialReportsApiClient> _official = new();

    public DashboardRecentTransactionsLinkTests()
    {
        var dashboardApi = new Mock<IDashboardApiClient>();
        dashboardApi.Setup(api => api.GetOverviewAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<DashboardOverviewDto>.Success(new DashboardOverviewDto(
                0m, 0m, 0, 0, 0, 0, 0,
                Array.Empty<DashboardFacilityDto>(),
                Array.Empty<DashboardTransactionDto>(),
                Array.Empty<DashboardDelinquentDto>())));
        Services.AddSingleton(dashboardApi.Object);

        var municipalitiesApi = new Mock<IMunicipalitiesApiClient>();
        municipalitiesApi.Setup(api => api.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalitiesApi.Object));

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(_official.Object);
        var facilitiesApi = Mock.Of<IFacilitiesApiClient>();
        Services.AddSingleton<IFacilitiesApiClient>(facilitiesApi);
        Services.AddSingleton(new FacilityState(facilitiesApi));
        this.AddTestAuthorization().SetNotAuthorized();
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [Fact]
    public void RecentTransactionsViewAll_OpensRegisteredAuditTrailRoute()
    {
        var cut = RenderComponent<Menu>();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find(".bottom-row .panel-header a.panel-link");
            Assert.Equal("/audit-trail", link.GetAttribute("href"));
        });
    }

    [Fact]
    public void TheDashboard_ShowsEverySourceFromTheSharedProjection_NotOnlyTheFacilities()
    {
        var landing = new RevenueSourcePerformanceRowDto("LANDING_BERTHING", "Landing / Berthing", "MARKET", "Income from Market",
            RevenueSourceModel.Transactional, "CT", null, 100m, 0m, 100m, 1, 1, 1, "Active", false);
        _official.Setup(x => x.GetSourcePerformanceAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(new RevenueSourcePerformanceDto(2026, 10,
                [new RevenueSourceGroupDto("MARKET", "Income from Market", 100m)], [landing], 100m, [], DateTime.UtcNow)));

        var cut = RenderComponent<Menu>();

        cut.WaitForAssertion(() =>
        {
            var row = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Landing / Berthing"));
            Assert.Contains("₱100", row.TextContent);
            Assert.Contains("1 transaction", row.TextContent);
            Assert.DoesNotContain("%", row.TextContent);
            // The old facility-only title is gone; the facility cards are a labelled secondary snapshot.
            Assert.DoesNotContain("Revenue by Facility", cut.Markup);
            Assert.Contains("Operations overview", cut.Markup);
            Assert.Contains("Outstanding", cut.Markup);
        });
    }
}
