using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities;
using EEMOCantilanSDS.Client.Components.Pages.Shared.ButtonGroup;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class SpaceRentalConsistencyTests : TestContext
{
    private readonly Mock<IStallsApiClient> _stalls = new();
    public SpaceRentalConsistencyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Head").SetRoles("SuperAdmin");
        Services.AddSingleton(_stalls.Object);
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([
                new(FacilityCode.NPM, "New Public Market", "NPM", 0, null, null, null),
                new(FacilityCode.NCC, "New Commercial Center", "NCC", 0, null, null, null),
                new(FacilityCode.ICE, "Ice Plant", "ICE", 0, null, null, null)]));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton<FacilityState>();
        Services.AddSingleton<BrandingState>();
        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(x => x.GetFacilityPaymentRecordsAsync(It.IsAny<FacilityCode>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilityPaymentRecordDto>>.Success([]));
        Services.AddSingleton(payments.Object);
    }

    [Theory]
    [InlineData("npm", "Stall")]
    [InlineData("ncc", "Stall")]
    [InlineData("tcc", "Stall")]
    [InlineData("bbq", "Stall")]
    [InlineData("ice", "Space")]
    [InlineData("custom1", "Space")]
    public void FacilityActionsUseOneVocabularyAndKeepTheirDestinations(string facility, string unit)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/facility/{facility}?facility={facility}");
        var cut = RenderComponent<ActionBar>();
        Assert.Equal($"{unit} Holder List", cut.FindAll("a")[0].TextContent.Trim());
        Assert.Equal($"/list-stall-holders?facility={facility}", cut.FindAll("a")[0].GetAttribute("href"));
        Assert.Equal($"Manage {unit} Holders", cut.FindAll("a")[1].TextContent.Trim());
        Assert.Equal($"/vendors?facility={facility}", cut.FindAll("a")[1].GetAttribute("href"));
    }

    [Fact]
    public void IcePilotKeepsItsSourceTableNavigationAndRegistration()
    {
        var stall = new StallDto(Guid.NewGuid(), "ICE-7", StallStatus.Active, "Actual ICE holder", "Signed lessee",
            12.5, new DateTime(2026, 1, 1), 1200m, null, null, null, null, null, null, 3);
        _stalls.Setup(x => x.GetStallsByFacilityPaginatedAsync(FacilityCode.ICE, null, null, 100))
            .ReturnsAsync(Result<CursorPagedResult<StallDto>>.Success(new() { Items = [stall] }));
        var cut = RenderComponent<ICEPLANT>();
        cut.WaitForAssertion(() => Assert.Contains("Actual ICE holder", cut.Markup));
        Assert.NotNull(cut.Find("a[href='/list-stall-holders?facility=ice']"));
        Assert.NotNull(cut.Find("a[href='/vendors?facility=ice']"));
        Assert.Contains("Total Spaces", cut.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("ice-overview", cut.Markup);
        var hero = cut.Find(".facility-hero").TextContent;
        cut.Find(".search-input").Input("absent");
        Assert.DoesNotContain("Actual ICE holder", cut.Markup);
        Assert.Equal(hero, cut.Find(".facility-hero").TextContent);
        cut.Find(".search-input").Input("ICE-7");
        Assert.Contains("Actual ICE holder", cut.Find("tbody").TextContent);
        cut.FindAll("button").Single(x => x.TextContent.Contains("Add New")).Click();
        Assert.Contains("Contract Duration", cut.Markup);
        Assert.Contains("Area", cut.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("ice-pilot-registration", cut.Markup);
        _stalls.Verify(x => x.GetStallsByFacilityPaginatedAsync(FacilityCode.ICE, null, null, 100), Times.Once);
    }

    [Fact]
    public void NccOverviewUsesStallRentAndKeepsItsExistingContractData()
    {
        var stall = new StallDto(Guid.NewGuid(), "N-7", StallStatus.Active, "Actual NCC holder", "Signed lessee",
            12.5, new DateTime(2026, 1, 1), 1200m, null, null, null, null, null, null, 3);
        _stalls.Setup(x => x.GetStallsByFacilityPaginatedAsync(FacilityCode.NCC, null, null, 100))
            .ReturnsAsync(Result<CursorPagedResult<StallDto>>.Success(new() { Items = [stall] }));
        var cut = RenderComponent<NCC>();
        cut.WaitForAssertion(() => Assert.Contains("Actual NCC holder", cut.Markup));
        Assert.Equal("Stall Rent", cut.Find(".rental-summary-title").TextContent);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("ncc-overview", cut.Markup);
        Assert.Contains("Total Stalls", cut.Markup);
        Assert.Equal("Search stall no. or occupant…", cut.Find(".search-input").GetAttribute("placeholder"));
        Assert.Equal("Stall Holder List", cut.Find("a[href='/list-stall-holders?facility=ncc']").TextContent.Trim());
        Assert.Equal("Manage Stall Holders", cut.Find("a[href='/vendors?facility=ncc']").TextContent.Trim());
        Assert.Contains("Signed lessee", cut.Markup);
        Assert.Equal(12.5, cut.FindComponent<EEMOCantilanSDS.Client.Components.Shared.FacilityPage<NCC.NccStall>>().Instance.StallMapper(stall, false, false, 0m, "").AreaSqm);
        Assert.Contains("Stall No.", cut.Find("thead").TextContent);
        Assert.DoesNotContain("All Spaces", cut.Markup);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("ncc-overview", cut.Markup);
    }

    [Fact]
    public void StallManagementDestinationActuallySelectsTheRequestedFacility()
    {
        _stalls.Setup(x => x.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Failure("Unavailable"));
        var vendors = new Mock<IVendorsApiClient>();
        vendors.Setup(x => x.GetVendorRegistryAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<VendorRegistryDto>.Success(new(0, 0, 0, 0, 0, 0, 0, 0m, 0m, [])));
        Services.AddSingleton(vendors.Object);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/vendors?facility=ncc");
        var cut = RenderComponent<Vendor>();
        cut.WaitForAssertion(() => Assert.Equal("NCC", cut.Find(".fac-trigger-label").TextContent.Trim()));
        Assert.Equal("Manage Stall Holders", cut.Find(".topbar-title").TextContent);
        Assert.Equal("New Commercial Center", cut.Find("a[href='/facility/ncc']").TextContent);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("ncc-management", cut.Markup);
        nav.NavigateTo("/vendors?facility=ice");
        Assert.Equal("ICE", cut.Find(".fac-trigger-label").TextContent.Trim());
        Assert.Equal("Manage Space Holders", cut.Find(".topbar-title").TextContent);
    }
}
