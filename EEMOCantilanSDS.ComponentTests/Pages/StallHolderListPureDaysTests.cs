using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Roster = EEMOCantilanSDS.Client.Components.Pages.Shared.SH.StallHoldersList;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public class StallHolderListPureDaysTests : TestContext
{
    private IRenderedComponent<Roster> Render(NpmMonthBasis basis)
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
                FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<StallHoldersListDto>.Success(dto));
        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Admin");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/list-stall-holders?facility=npm");

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
}
