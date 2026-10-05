using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Operation reports share one shell (letterhead, classification, instrument, period, Print, CSV where real) and adapt the
/// body: transactional governed operations show the official Monthly Income row and the month's register with no
/// "outstanding"; Arrears reads existing receivables only and states no fixed instrument.
/// </summary>
public sealed class OperationReportsAndArrearsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficialReportsApiClient> _official = new();
    private readonly Mock<IGovernedServicesApiClient> _governed = new();
    private readonly Mock<IReportsApiClient> _reports = new();
    private readonly Mock<IObligationsApiClient> _obligations = new();

    public OperationReportsAndArrearsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_official.Object);
        Services.AddSingleton(_governed.Object);
        Services.AddSingleton(_reports.Object);
        Services.AddSingleton(_obligations.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static OfficialMonthlyIncomeDto Statement(string rowKey, int month, decimal amount)
    {
        var cells = Enumerable.Range(1, 12).Select(m => new MonthlyIncomeCellDto(0m, m == month ? amount : 0m)).ToList();
        var row = new OfficialMonthlyIncomeRowDto(rowKey, rowKey, rowKey, cells, new MonthlyIncomeCellDto(0m, amount), "Canonical", null, null);
        var group = new OfficialMonthlyIncomeGroupDto("MARKET", "Income from Market", [row], cells, new MonthlyIncomeCellDto(0m, amount));
        return new OfficialMonthlyIncomeDto(2026, null, [group], cells, new MonthlyIncomeCellDto(0m, amount), false, [], DateTime.UtcNow);
    }

    [Fact]
    public void ATransactionalReport_StatesTheServersMonthlyIncomeRowAndRegister_WithNoOutstanding()
    {
        var month = DateTime.Today.Month;
        _official.Setup(x => x.GetMonthlyIncomeAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement("MARKET_FEES", month, 90m)));
        _governed.Setup(x => x.GetActivityAsync("MARKET_FEES", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success(
            [
                new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), DateTime.UtcNow, "CT-000101", RevenueInstrumentType.CashTicket,
                    null, "Walk-up", "Stall 4", 30m, "Ana Reyes", "Posted"),
            ]));

        var cut = RenderComponent<MarketFeesReport>();

        cut.WaitForAssertion(() =>
        {
            var official = cut.Find("section[aria-labelledby='mfr-official'] tbody tr").TextContent;
            Assert.Contains("90.00", official);                         // the server's Monthly Income row, not a typed figure
            Assert.Contains("CT-000101", cut.Find("section[aria-labelledby='mfr-register']").TextContent);
            Assert.DoesNotContain("Outstanding", cut.Find("article").TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.HasAttribute("download"));   // a real CSV of real rows
            Assert.Contains("Cash Ticket", cut.Find(".lh-meta").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheVegetableReport_StatesBothInstruments_AndTheModeThatDecides()
    {
        _governed.Setup(x => x.GetActivityAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success([]));

        var cut = RenderComponent<VegetableFruitReport>();

        cut.WaitForAssertion(() =>
        {
            var facts = cut.Find(".lh-meta").TextContent;
            Assert.Contains("Official Receipt for a whole payment", facts);
            Assert.Contains("Cash Ticket for a daily transaction", facts);
            Assert.Contains("Mode", cut.Find("section[aria-labelledby='vfr-register'] thead").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheTransportationReport_IsACashTicketReport()
    {
        var cut = RenderComponent<TransportationReport>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cash Ticket", cut.Find(".lh-meta").TextContent);
            Assert.DoesNotContain("Official Receipt", cut.Find(".lh-meta").TextContent);
        }, Timeout);
    }

    private void ArrearsData()
    {
        _reports.Setup(x => x.GetFinancialReportAsync(ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), null, false))
            .ReturnsAsync(Result<FinancialReportDto>.Success(ReportPageTests.SampleReport() with
            {
                DelinquentAccountsTotal = 7,
                DelinquentOutstandingTotal = 12_345m,
            }));
        _reports.Setup(x => x.GetFollowUpQueueAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<FollowUpQueueDto>.Success(new FollowUpQueueDto("October 2026", DateOnly.FromDateTime(DateTime.Today),
            [
                new(1, "Critical", "Delinquent", "delinquent", FacilityCode.NCC, "Monthly rental", "Rosa Magbanua", "Stall 3", 2_700m,
                    false, "3 months to September 2026", "Unpaid · 3 months", "Record payment", "/profile/ncc/3"),
                new(1, "Normal", "Current-period", "current", FacilityCode.TCC, "Monthly rental", "Current Only", "Stall 9", 900m,
                    false, "October 2026", "Unpaid", "Record payment", "/profile/tcc/9"),
            ])));
    }

    [Fact]
    public void TheArrearsWorkspace_ReadsExistingReceivables_AndOffersNoWriteOff()
    {
        ArrearsData();

        var cut = RenderComponent<Arrears>();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            Assert.Contains("₱12,345.00", markup);            // the server's own delinquency total, not a client sum
            Assert.Contains("Rosa Magbanua", markup);
            Assert.DoesNotContain("Current Only", markup);     // current-period balances are not arrears
            Assert.Contains("/profile/ncc/3", markup);         // opens the existing account
            var actions = string.Join(" | ", cut.FindAll("button, a").Select(e => e.TextContent));
            foreach (var action in new[] { "Write off", "Forgive", "Adjust" })
                Assert.DoesNotContain(action, actions, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
    }

    [Fact]
    public void TheArrearsReport_StatesNoFixedInstrument()
    {
        ArrearsData();

        var cut = RenderComponent<ArrearsReport>();

        cut.WaitForAssertion(() =>
        {
            var facts = cut.Find(".lh-meta").TextContent;
            Assert.Contains("Arrears", facts);
            Assert.DoesNotContain("Instrument", facts);
            Assert.DoesNotContain("Official Receipt", facts);
            Assert.DoesNotContain("Cash Ticket", facts);
            Assert.Contains("Rosa Magbanua", cut.Find("section[aria-labelledby='arrr-register']").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheFishMeatVendorFeeReport_PreservesHistoricalAccounts_SeparateFromCurrentDirectCollections()
    {
        _obligations.Setup(x => x.GetAccountsAsync(ObligationKind.FishMeatVendorFee))
            .ReturnsAsync(Result<IReadOnlyList<ObligationAccountDto>>.Success(
            [
                new(Guid.NewGuid(), ObligationKind.FishMeatVendorFee, "Fish / Meat Vendor Fee", Guid.NewGuid(), "Lito Tan", Guid.NewGuid(), "F-3",
                    "Fish stall F-3", null, null, new DateOnly(2026, 1, 1), null, 300m, new DateOnly(2026, 1, 1), 2_700m, 2_100m, 600m),
            ]));

        var cut = RenderComponent<FishMeatVendorFeesReport>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Official Receipt", cut.Find(".lh-meta").TextContent);
            var position = cut.Find("section[aria-labelledby='fmvr-position']").TextContent;
            Assert.Contains("₱600.00", position);              // the account's server outstanding, not a recomputation
            Assert.Contains("Historical account position", position);
            Assert.Contains("Historical monthly amount", cut.Find("section[aria-labelledby='fmvr-accounts'] thead").TextContent);
            Assert.Contains("Additional vendor fee received", cut.Markup);
            Assert.DoesNotContain("Daily Fee", cut.Markup);
            Assert.Contains("separate from NPM stall rent", cut.Markup);
        }, Timeout);
    }

    [Theory]
    [InlineData(typeof(FishMeatVendorFeesReport), "/operations/fish-meat-vendor-fees/report")]
    [InlineData(typeof(KanmanggayReport), "/operations/kanmanggay/report")]
    [InlineData(typeof(FiestaArawReport), "/operations/fiesta-araw/report")]
    [InlineData(typeof(Arrears), "/operations/arrears")]
    [InlineData(typeof(ArrearsReport), "/operations/arrears/report")]
    [InlineData(typeof(TransportationReport), "/operations/transportation/report")]
    [InlineData(typeof(TransferLargeCattleReport), "/operations/transfer-large-cattle/report")]
    [InlineData(typeof(VegetableFruitReport), "/operations/vegetable-fruit/report")]
    public void TheNewOperationPagesHaveTheirRoutes(Type page, string route)
    {
        var routes = page.GetCustomAttributes(typeof(RouteAttribute), inherit: true).Cast<RouteAttribute>().Select(r => r.Template);
        Assert.Contains(route, routes);
    }
}
