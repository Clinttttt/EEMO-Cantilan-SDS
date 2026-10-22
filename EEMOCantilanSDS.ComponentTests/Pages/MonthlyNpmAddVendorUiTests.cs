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

public sealed class MonthlyNpmAddVendorUiTests : TestContext
{
    [Theory]
    [InlineData(BillingArchetype.MonthlyRental, false)]
    [InlineData(BillingArchetype.DailyStall, true)]
    public async Task CustomMarketAreaAsksForDailyRateOnlyInDailyModel(BillingArchetype archetype, bool dailyRateExpected)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new[]
            {
                new FacilitySidebarSummaryDto(FacilityCode.NPM, "Carrascal Public Market", "CPM", 0,
                    Archetype: archetype)
            }));
        var catalog = new FacilityState(api.Object);
        await catalog.EnsureLoadedAsync();
        Services.AddSingleton(catalog);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();

        var cut = RenderComponent<AddVendorModal>(x => x
            .Add(c => c.Show, true)
            .Add(c => c.Form, new AddVendorModal.VendorModalForm
            {
                FacilityCode = "NPM", SelectedSection = "__custom__", CustomSectionName = "Dry goods"
            }));

        Assert.Equal(dailyRateExpected, cut.FindAll("input[placeholder='e.g. 30']").Count > 0);
        Assert.NotEmpty(cut.FindAll("input[placeholder='e.g. 900']"));
    }
}
