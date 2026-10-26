using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ImportStallholders = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.ImportStallholders;

namespace EEMOCantilanSDS.Testing;

/// <summary>The stallholder import accepts only the office's own ACTIVE Stall Rent custom facilities, by slot code or acronym.</summary>
public class ImportStallholdersCustomFacilityTests : TestContext
{
    private IRenderedComponent<ImportStallholders> Render(string facility)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallHoldersListAsync(It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>()))
              .ReturnsAsync(Result<StallHoldersListDto>.Success(new StallHoldersListDto()));
        // The catalogue lists active facilities only: Custom3 is deliberately absent (inactive / never declared).
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
           {
               new(FacilityCode.Custom1, "Riverside Hall", "RH", 0),
               new(FacilityCode.Custom2, "Night Spaces", "NS", 0, CustomCategory: CustomFacilityCategory.SpaceRental),
           }));

        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IOfficeSourcesApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton<FacilityState>();
        this.AddTestAuthorization().SetAuthorized("Admin");
        return RenderComponent<ImportStallholders>(p => p.Add(c => c.Facility, facility));
    }

    [Theory]
    [InlineData("custom1")]
    [InlineData("rh")]
    public void ActiveStallRentCustomFacility_ShowsTheUploadStepInTheOfficesOwnWords(string slug)
    {
        var cut = Render(slug);
        Assert.Empty(cut.FindAll(".imp-state-title"));
        Assert.Contains("Riverside Hall", cut.Markup);
        Assert.Contains("RH-stallholders-template.csv", cut.Markup);
        Assert.Contains("href=\"/facility/rh\"", cut.Markup);
    }

    [Theory]
    [InlineData("custom2")]   // Space Rental category
    [InlineData("custom3")]   // not active / not declared by this office
    [InlineData("custom4")]
    [InlineData("nonsense")]
    public void OtherCustomFacilities_AreRefusedWithTheNotAvailableState(string slug)
    {
        var cut = Render(slug);
        Assert.Contains("Import not available", cut.Find(".imp-state-title").TextContent);
    }
}
