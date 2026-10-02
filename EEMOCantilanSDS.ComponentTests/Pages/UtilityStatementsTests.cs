using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>ECF and WCF own their statements of account: read from their own obligation reads, with no NPM dependency.</summary>
public sealed class UtilityStatementsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid Stall = Guid.NewGuid();

    public UtilityStatementsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    [Fact]
    public void Ecf_StatementsLoadFromTheEcfRead_StateAssessedPaidAndBalance_AndNeedNoNpmView()
    {
        Assert.Equal("/operations/ecf/statements",
            Assert.Single(typeof(ElectricityConsumptionFeesStatements).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<EcfObligationQuoteDto>>.Success([Quote("Pedro Vendor", 650m, 150m, 500m)]));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<ElectricityConsumptionFeesStatements>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='ECF statement register'] tbody tr"));
            Assert.Contains("Pedro Vendor", row.TextContent);
            Assert.Contains("₱650.00", row.TextContent);
            Assert.Contains("₱150.00", row.TextContent);
            Assert.Contains("₱500.00", row.TextContent);
            Assert.Equal("Statements", cut.Find("h2").TextContent.Trim());
            var sheet = cut.Find("article.us-sheet");
            Assert.Contains("Statement of Account — Electricity Consumption Fee (ECF)", sheet.TextContent);
            Assert.Contains("Official Receipt", sheet.TextContent);
            Assert.DoesNotContain("utilities", cut.Markup.ToLowerInvariant().Replace("utility", ""));
        }, Timeout);
    }

    [Fact]
    public void Wcf_BalanceOnlyFilterHidesSettledStatements()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(
            Result<IReadOnlyList<WcfObligationQuoteDto>>.Success([WcfQuote("Ana Stall", 100m, 100m, 0m), WcfQuote("Ben Stall", 100m, 0m, 100m)]));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFeesStatements>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[aria-label='WCF statement register'] tbody tr").Count), Timeout);

        cut.Find("input[type='checkbox']").Change(true);
        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='WCF statement register'] tbody tr"));
            Assert.Contains("Ben Stall", row.TextContent);
            Assert.Contains("Cash Ticket", cut.Find("article.us-sheet").TextContent);
        }, Timeout);
    }

    private static EcfObligationQuoteDto Quote(string payor, decimal assessed, decimal settled, decimal outstanding) => new(
        Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 1, Stall, "A-1", "NPM", "Dry Goods",
        2026, 10, 0m, 0m, 0m, 0m, assessed, settled, outstanding, SettlementAuthority.Legacy, null, payor,
        Guid.NewGuid(), Guid.NewGuid(), "ECF", RevenueInstrumentType.OfficialReceipt, "Direct", true, true);

    private static WcfObligationQuoteDto WcfQuote(string payor, decimal assessed, decimal settled, decimal outstanding) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "B-" + payor[..1], "NPM", "Fish", 2026, 10, 0m, 0m, 0m, 0m, assessed, settled,
        outstanding, SettlementAuthority.Legacy, null, payor, Guid.NewGuid(), Guid.NewGuid(), "WCF", RevenueInstrumentType.CashTicket, "Direct", true);
}
