using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Roster = EEMOCantilanSDS.Client.Components.Pages.Shared.SH.StallHoldersList;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public class StallHolderListPureDaysTests : TestContext
{
    private IRenderedComponent<Roster> Render(NpmMonthBasis basis, string facility = "npm")
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var dto = new StallHoldersListDto
        {
            MonthBasis = basis,
            Sections =
            [
                new StallHoldersSectionDto
                {
                    SectionName = "Vegetable Area",
                    Rows =
                    [
                        new StallHolderRowDto
                        {
                            StallNo = "7",
                            ActualOccupant = "Tenant A",
                            NameOnContract = "Tenant A",
                            EffectivityDate = new DateOnly(2026, 1, 1),
                            DurationYears = 1,
                            AreaSqm = 4,
                            MonthlyRentalRate = 900m,
                            ActualMonthlyRental = 900m,
                            WholeYearRental = 10_800m,
                            DailyRate = 47m,
                        },
                    ],
                },
            ],
        };
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(s => s.GetStallHoldersListAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<StallHoldersListDto>.Success(dto));
        Services.AddSingleton(stalls.Object);
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(x => x.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([
                new(FacilityCode.NPM, "New Public Market", "NPM", 0, null, null, null),
                new(FacilityCode.NCC, "New Commercial Center", "NCC", 0, null, null, null)]));
        Services.AddSingleton(new EEMOCantilanSDS.Client.Services.FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Admin");
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/list-stall-holders?facility={facility}");

        return RenderComponent<Roster>();
    }

    [Fact]
    public void PureDaysShowsDailySemanticsAndNoFixedRentClaim()
    {
        var cut = Render(NpmMonthBasis.PureDays);

        Assert.Contains("Calendar-day", cut.Markup);
        Assert.Contains("₱47.00/day", cut.Markup);
        Assert.DoesNotContain("Monthly Rentals", cut.Markup);
        Assert.DoesNotContain("Whole Year Rental", cut.Markup);
    }

    [Fact]
    public void RentGoalKeepsTheExistingMonthlyAndAnnualPresentation()
    {
        var cut = Render(NpmMonthBasis.RentGoal);

        Assert.Contains("Monthly Rentals", cut.Markup);
        Assert.Contains("Actual Mo.", cut.Markup);
        Assert.Contains("Whole Year", cut.Markup);
        Assert.Contains("₱10,800.00", cut.Markup);
    }

    [Theory]
    [InlineData("npm", "Stall")]
    [InlineData("ncc", "Stall")]
    [InlineData("tcc", "Stall")]
    [InlineData("bbq", "Stall")]
    [InlineData("ice", "Space")]
    [InlineData("custom1", "Space")]
    public void FacilityTerminologyIsConsistentAcrossChromeDocumentAndSearch(string facility, string noun)
    {
        var cut = Render(NpmMonthBasis.RentGoal, facility);
        Assert.Equal($"{noun} Holder List", cut.Find(".topbar-title").TextContent);
        Assert.Equal($"{noun} Holder List", cut.Find(".breadcrumb-active").TextContent);
        Assert.Equal($"{noun} Holder List", cut.Find(".lsh-doc-title").TextContent);
        Assert.Equal($"Search name or {noun.ToLowerInvariant()} no…", cut.Find("input").GetAttribute("placeholder"));
        Assert.Equal($"Total {noun}s", cut.Find(".lsh-hero-key").TextContent);
        Assert.Contains("Name of Lessee", cut.Find("thead").TextContent);
        Assert.Contains("No. of", cut.Find("thead").TextContent);
        if (facility == "ncc") Assert.Contains("Area Loc.", cut.Find("thead").TextContent);
        Assert.Contains("stallholders_" + facility, cut.Find("a[download]").GetAttribute("download"));
        if (facility == "ncc") SpaceHolderWorkspaceTests.SaveVisualArtifact("ncc-roster", cut.Markup);
    }
}
