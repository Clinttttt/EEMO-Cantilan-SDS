using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Client.Components.Pages.Shared.SH;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class SlaughterClientRecordTests : TestContext
{
    [Fact]
    public void PrintedRecordPreservesTransactionAndChargeEvidence()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<FacilityState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        JSInterop.Mode = JSRuntimeMode.Loose;
        var api = new Mock<ISlaughterApiClient>();
        api.Setup(x => x.GetAnimalLabelsAsync()).ReturnsAsync(Result<SlaughterAnimalLabelsDto>.Success(new("Hog", "Carabao", "Cow")));
        api.Setup(x => x.GetClientProfileAsync("Test client")).ReturnsAsync(Result<ClientProfileDto>.Success(new(
            "Test client", 1, 1, 365m,
            [new(new(2026, 10, 27), "Carabao", 1, 365m, 365m, "SRC-2026-000038", "Test collector",
                [new("Slaughter Fee", 1m, 150m, 150m), new("Slaughter Permit", 1m, 100m, 100m),
                 new("Table Charge", 1m, 30m, 30m), new("Ante-mortem Inspection", 1m, 20m, 20m),
                 new("Livestock Fee", 1m, 40m, 40m), new("Post-mortem Inspection", 1m, 25m, 25m)])],
            new(1, 1, 365m, 365m))));
        Services.AddSingleton(api.Object);
        var cut = RenderComponent<SlaughterViewDetails>(p => p.Add(x => x.OwnerName, "Test client"));
        cut.WaitForAssertion(() => Assert.Equal(6, cut.FindAll(".txn-item-table tbody tr").Count));
        Assert.Contains("SRC-2026-000038", cut.Find(".txn-table").TextContent);
        Assert.Contains("365.00", cut.Find(".txn-item-table tfoot").TextContent);
        cut.Find(".btn-print").Click();
        Assert.Contains(JSInterop.Invocations, x => x.Identifier == "stalltrackPrint.reportDocument");
        SpaceHolderWorkspaceTests.SaveVisualArtifact("slaughter-client-record", cut.Markup);
    }
}
