using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The Operations workspaces share the civic-blue hero: one identity band whose figures are the page's own loaded figures, with
/// no second summary strip repeating them, and with the screen-only hero never reaching a printed statement.
/// </summary>
public sealed class OperationsWorkspaceHeroTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public OperationsWorkspaceHeroTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton(Mock.Of<IGovernedServicesApiClient>());
        Services.AddSingleton(Mock.Of<ICollectorsApiClient>());
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private static EcfObligationQuoteDto Ecf(string payer, decimal assessed, decimal settled, decimal outstanding) => new(
        Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 1,
        Guid.NewGuid(), "A-1", "Tenant Market", "Dry Goods", 2026, 9, 100m, 110m, 10m, 10m,
        assessed, settled, outstanding, SettlementAuthority.Canonical, null, payer,
        Guid.NewGuid(), Guid.NewGuid(), "ECF", RevenueInstrumentType.OfficialReceipt, "Metered", true, true);

    private void ServeEcf(Result<IReadOnlyList<EcfObligationQuoteDto>> result)
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(result);
        Services.AddSingleton(api.Object);
    }

    [Fact]
    public void EcfStatements_TheHeroIsScreenOnly_AndTheBillingMonthSurvivesALoadFailure()
    {
        ServeEcf(Result<IReadOnlyList<EcfObligationQuoteDto>>.Failure("offline"));

        var cut = RenderComponent<ElectricityConsumptionFeesStatements>();

        cut.WaitForAssertion(() => Assert.Contains("couldn't be loaded", cut.Find("[role=alert]").TextContent), Timeout);
        Assert.NotNull(cut.Find("input[type=month]"));              // the office can still choose another month
        Assert.Equal("Electricity Consumption Fees Statements", cut.Find("h1.v3h-title").TextContent.Trim());
        Assert.NotNull(cut.Find("h1.v3h-title").Closest(".no-print"));
    }

    [Fact]
    public void EcfStatements_ThePrintedSheetsCarryTheLetterheadAndNeverTheHero()
    {
        ServeEcf(Result<IReadOnlyList<EcfObligationQuoteDto>>.Success([Ecf("Ana Reyes", 100m, 40m, 60m)]));

        var cut = RenderComponent<ElectricityConsumptionFeesStatements>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".us-sheets")), Timeout);
        Assert.Empty(cut.FindAll(".us-sheets .v3h"));
        Assert.All(cut.FindAll(".v3h"), hero => Assert.NotNull(hero.Closest(".no-print")));
        Assert.Contains("₱60.00", cut.Find("dl.v3h-figures").TextContent);
        var bar = cut.Find("[aria-label='Statement filters']");
        Assert.NotNull(bar.QuerySelector("input[type=month]"));
        Assert.NotNull(bar.QuerySelector("input[type=search]"));
        Assert.Contains("Print statements", bar.TextContent);
    }

    [Fact]
    public void TransferLargeCattle_UsesTheBlueHeroAsIdentityOnly()
    {
        var governed = new Mock<IGovernedServicesApiClient>();
        governed.Setup(x => x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<GovernedServiceDefinitionDto>>.Success([]));
        governed.Setup(x => x.GetActivityAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success([]));
        Services.AddSingleton(governed.Object);

        var cut = RenderComponent<TransferLargeCattle>();

        Assert.Equal("Transfer Large Cattle", cut.Find("h1.v3h-title").TextContent.Trim());
        Assert.Empty(cut.FindAll("dl.v3h-figures"));                // no second source of figures beside the workspace's own reads
        Assert.Empty(cut.FindAll(".wh"));
        Assert.Equal("/operations/transfer-large-cattle/report", cut.Find(".v3h-actions a").GetAttribute("href"));
    }

    [Fact]
    public void SpaceImports_UseTheBlueHeroWithTheirThreeSteps()
    {
        var blue = RenderComponent<VegetableFruitImport>();

        Assert.NotNull(blue.Find(".imp-hero.imp-hero-blue"));
        Assert.Equal(3, blue.FindAll(".imp-step").Count);
        Assert.Contains("active", blue.Find(".imp-step").ClassList);
        Assert.Contains("Upload", blue.Find(".imp-steps").TextContent);

        // Space and stallholder imports share the established blue operation hero.
        Assert.NotNull(RenderComponent<KanmanggayImport>().Find(".imp-hero-blue"));
        Assert.NotNull(RenderComponent<FiestaArawImport>().Find(".imp-hero-blue"));
    }
}
