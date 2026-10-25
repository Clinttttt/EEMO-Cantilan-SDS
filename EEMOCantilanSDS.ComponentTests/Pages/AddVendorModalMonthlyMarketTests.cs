using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// A market its LGU rents by the month is stall rent only: the form states no daily fee, works no daily figure out of the rent,
/// and lets the stall be filed under no section, as the server allows. A daily market keeps the section required.
/// </summary>
public class AddVendorModalMonthlyMarketTests : TestContext
{
    private async Task<IRenderedComponent<AddVendorModal>> RenderForm(BillingArchetype marketArchetype, AddVendorModal.VendorModalForm form)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
            {
                new(FacilityCode.NPM, "Madrid Public Market", "MPM", 0, Archetype: marketArchetype),
            }));
        var catalog = new FacilityState(api.Object);
        await catalog.EnsureLoadedAsync();

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(catalog);

        return RenderComponent<AddVendorModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.Form, form)
            .Add(c => c.NpmDailyRate, 30m)
            .Add(c => c.NpmFishRate, 1m));
    }

    private static AddVendorModal.VendorModalForm Stall(string section) => new() { FacilityCode = "NPM", StallNo = "12", SelectedSection = section };

    [Fact]
    public async Task AMonthlyMarketStatesStallRentOnly_WithNoDailyFeeOrDailyFigure()
    {
        var form = Stall("Fish");
        var cut = await RenderForm(BillingArchetype.MonthlyRental, form);

        Assert.DoesNotContain("/day", cut.Markup);
        Assert.DoesNotContain("per kilo", cut.Markup);
        Assert.Contains("Madrid Public Market", cut.Markup);

        cut.FindAll("input.avm-input").First(i => i.GetAttribute("placeholder") == "e.g. 900").Change("900");

        Assert.Equal(0m, form.CustomDailyRate);
    }

    [Fact]
    public async Task AMonthlyMarketOffersNoSection_AndDoesNotMarkTheSectionRequired()
    {
        var cut = await RenderForm(BillingArchetype.MonthlyRental, Stall(""));

        var label = cut.FindAll("label.avm-label").First(l => l.TextContent.Contains("Section / Category"));
        Assert.Empty(label.QuerySelectorAll(".avm-req"));
        Assert.Contains("No section", cut.Markup);
    }

    [Fact]
    public async Task ADailyMarketStillRequiresASection()
    {
        var cut = await RenderForm(BillingArchetype.DailyStall, Stall("Vegetable"));

        var label = cut.FindAll("label.avm-label").First(l => l.TextContent.Contains("Section / Category"));
        Assert.NotEmpty(label.QuerySelectorAll(".avm-req"));
        Assert.DoesNotContain("No section", cut.Markup);
    }
}
