using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class DashboardRecentTransactionsLinkTests : TestContext
{
    [Fact]
    public void RecentTransactionsViewAll_OpensRegisteredAuditTrailRoute()
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
        var facilitiesApi = Mock.Of<IFacilitiesApiClient>();
        Services.AddSingleton<IFacilitiesApiClient>(facilitiesApi);
        Services.AddSingleton(new FacilityState(facilitiesApi));
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<Menu>();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find(".bottom-row .panel-header a.panel-link");
            Assert.Equal("/audit-trail", link.GetAttribute("href"));
        });
    }
}
