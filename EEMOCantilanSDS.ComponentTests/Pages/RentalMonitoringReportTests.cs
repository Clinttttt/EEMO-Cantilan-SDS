using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class RentalMonitoringReportTests : TestContext
{
    [Fact]
    public void MonitoringUsesServerAssessmentAndTotals_AndRequestsYearlyAuthorityInsteadOfMultiplyingTheRate()
    {
        var api = new Mock<IFacilitiesApiClient>();
        var row = new StallComplianceDto(Guid.NewGuid(), "1", "Space occupant", "", "", "", 100m, 0m,
            "Partial", 150m, 75m, null, 0, 0, new DateOnly(2026, 8, 15), 0, 225m, AccountAssessment: 225m);
        var report = new FacilityReportsDto(150m, 0m, 0m, 0m, 1, 1, 1, 75m, [], null!, [], [], null!, null, null, [],
            [row], new MonitoringTotalsDto(225m, 150m, 75m));
        api.Setup(a => a.GetFacilityReportsAsync(FacilityCode.NCC, It.IsAny<ReportPeriod>(), 2026, It.IsAny<int?>()))
            .ReturnsAsync(Result<FacilityReportsDto>.Success(report));
        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<FacilityState>();
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RentalMonitoringReport>(p => p.Add(c => c.Code, FacilityCode.NCC)
            .Add(c => c.Year, 2026).Add(c => c.Month, 10));
        cut.WaitForAssertion(() => Assert.Contains("225.00", cut.Find("tbody").TextContent));
        Assert.Contains("Space holder", cut.Find("thead").TextContent);
        Assert.Contains("225.00", cut.Find("tfoot").TextContent);
        Assert.DoesNotContain("1,200", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent == "Whole year").Click();
        cut.WaitForAssertion(() => api.Verify(a => a.GetFacilityReportsAsync(FacilityCode.NCC, ReportPeriod.Yearly, 2026, null), Times.Once));
        cut.FindAll("button").Single(b => b.TextContent == "Print").Click();
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "stalltrackPrint.landscape");
    }
}
