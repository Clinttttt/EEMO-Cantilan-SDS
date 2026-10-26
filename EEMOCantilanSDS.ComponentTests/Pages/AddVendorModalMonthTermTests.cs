using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>A contract term may carry months only on an office's own Custom Space Rental facility; every other facility keeps whole years.</summary>
public class AddVendorModalMonthTermTests : TestContext
{
    private IRenderedComponent<AddVendorModal> RenderFor(string code, CustomFacilityCategory category, int years = 3, int months = 0, bool editing = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
           {
               new(Enum.Parse<FacilityCode>(code), "Cold Storage", "CS", 0, CustomCategory: category),
           }));
        var catalog = new FacilityState(api.Object);
        catalog.EnsureLoadedAsync().GetAwaiter().GetResult();

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(catalog);

        var form = new AddVendorModal.VendorModalForm
        {
            FacilityCode = code,
            Arrangement = OccupancyArrangement.SignedContract,
            FeeTypes = new List<string>(),
            ContractYears = years,
            ContractMonths = months,
        };
        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.IsEditing, editing)
            .Add(c => c.Form, form)
            .Add(c => c.ExistingStallNos, Array.Empty<string>())
            .Add(c => c.NpmFishRate, 1m));
    }

    [Fact]
    public void ACustomSpaceRentalFacility_OffersMonthsBesideYears_AndNoWholeYears()
    {
        var cut = RenderFor("Custom1", CustomFacilityCategory.SpaceRental, years: 0, months: 6);

        Assert.Contains("Plus months (optional)", cut.Markup);
        Assert.Contains("Term: 6 months.", cut.Markup);
        Assert.Contains("No whole years", cut.Markup);
    }

    [Fact]
    public void ACustomStallRentFacility_StaysOnWholeYears()
    {
        var cut = RenderFor("Custom1", CustomFacilityCategory.StallRent);

        Assert.DoesNotContain("Plus months", cut.Markup);
        Assert.DoesNotContain("No whole years", cut.Markup);
        Assert.Contains("Contract Duration (No. of Years)", cut.Markup);
    }

    [Fact]
    public void AnExistingFacilityLikeTcc_StaysOnWholeYears()
    {
        var cut = RenderFor("TCC", CustomFacilityCategory.StallRent);

        Assert.DoesNotContain("Plus months", cut.Markup);
    }

    [Fact]
    public void ARegisteredMonthTermIsShownInPlainWordsWhenTheFormIsEditing()
    {
        var cut = RenderFor("Custom1", CustomFacilityCategory.SpaceRental, years: 1, months: 6, editing: true);

        Assert.Contains("1 year 6 months", cut.Markup);
    }
}
