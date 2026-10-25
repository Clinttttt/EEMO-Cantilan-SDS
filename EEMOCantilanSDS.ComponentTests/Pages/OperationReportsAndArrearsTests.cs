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
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
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

        cut.OpenStatusReport();

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
    public void TheMarketFeesReport_BreaksTheMonthDownByFeeType_FromTheServersTotals()
    {
        _governed.Setup(x => x.GetActivityAsync("MARKET_FEES", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success(
            [
                new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), DateTime.UtcNow, "SRC-2026-000004", RevenueInstrumentType.CashTicket,
                    null, "Walk-up", null, 200m, "Ana Reyes", "Posted", "Comfort Room"),
            ]));
        _governed.Setup(x => x.GetFeeOptionTotalsAsync("MARKET_FEES", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<FeeOptionTotalDto>>.Success(
            [
                new(Guid.NewGuid(), "Comfort Room", 2, 234m),
                new(Guid.NewGuid(), "Weighing", 1, 50m),
            ]));

        var cut = RenderComponent<MarketFeesReport>();

        cut.OpenStatusReport();

        cut.WaitForAssertion(() =>
        {
            var fees = cut.Find("section[aria-labelledby='mfr-fees']");
            Assert.Contains("Comfort Room", fees.TextContent);
            Assert.Contains("₱284.00", fees.QuerySelector("tfoot")!.TextContent);   // the sum of the server's per-fee-type totals
            var register = cut.Find("section[aria-labelledby='mfr-register']");
            Assert.Contains("Fee type", register.QuerySelector("thead")!.TextContent);
            Assert.DoesNotContain("Instrument", register.QuerySelector("thead")!.TextContent);   // one instrument: stated once in the header
        }, Timeout);
    }

    [Fact]
    public void TheVegetableReport_StatesBothInstruments_AndTheModeThatDecides()
    {
        _governed.Setup(x => x.GetActivityAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success([]));

        var cut = RenderComponent<VegetableFruitReport>();

        cut.OpenStatusReport();

        cut.WaitForAssertion(() =>
        {
            var facts = cut.Find(".lh-meta").TextContent;
            Assert.Contains("Official Receipt for Monthly accounts", facts);
            Assert.Contains("Cash Ticket for daily transactions", facts);
            Assert.Contains("Obligation position", cut.Find("section[aria-labelledby='vfr-position']").TextContent);
            Assert.Equal("/operations/vegetable-fruit/daily-report", cut.Find("a[href$='/daily-report']").GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void TheTransportationReport_IsACashTicketReport()
    {
        var cut = RenderComponent<TransportationReport>();
        cut.OpenStatusReport();

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
                ArrearsAccountsTotal = 2,
                ArrearsOutstandingTotal = 8_000m,
            }));
        var queue = Result<FollowUpQueueDto>.Success(new FollowUpQueueDto("October 2026", DateOnly.FromDateTime(DateTime.Today),
            [
                new(1, "Critical", "Delinquent", "delinquent", FacilityCode.NCC, "Monthly rental", "Rosa Magbanua", "Stall 3", 2_700m,
                    false, "3 months to September 2026", "Unpaid · 3 months", "Record payment", "/profile/ncc/3"),
                new(1, "High", "Arrears", "arrears", FacilityCode.TCC, "Monthly rental", "Pedro Reyes", "Stall 4", 8_000m,
                    false, "Nov 2025 – Dec 2025", "Arrears · 2 months · previous year", "Record payment", "/profile/tcc/4"),
                new(1, "Normal", "Current-period", "current", FacilityCode.TCC, "Monthly rental", "Current Only", "Stall 9", 900m,
                    false, "October 2026", "Unpaid", "Record payment", "/profile/tcc/9"),
            ]));
        _reports.Setup(x => x.GetFollowUpQueueAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(queue);
        _reports.Setup(x => x.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(queue);
    }

    [Fact]
    public void TheArrearsWorkspace_ReadsExistingReceivables_AndOffersNoWriteOff()
    {
        ArrearsData();

        var cut = RenderComponent<Arrears>();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            Assert.Contains("₱20,345.00", markup);            // delinquent ₱12,345 + Arrears ₱8,000, the two server totals
            Assert.Contains("Rosa Magbanua", markup);
            Assert.DoesNotContain("Current Only", markup);     // current-period balances are not arrears
            Assert.Contains("Review account", markup);
            var actions = string.Join(" | ", cut.FindAll("button, a").Select(e => e.TextContent));
            foreach (var action in new[] { "Write off", "Forgive", "Adjust" })
                Assert.DoesNotContain(action, actions, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
    }


    [Fact]
    public void TheArrearsHero_StatesPositionApartFromRecoveredCash_AndNamesTheTwoScopesWhenTheirCountsDiffer()
    {
        ArrearsData();

        var cut = RenderComponent<Arrears>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Arrears & Delinquency", cut.Find("h1.v3h-title").TextContent.Trim());
            var figures = cut.Find("dl[aria-label='Delinquency position']").TextContent;
            Assert.Contains("₱20,345.00", figures);
            Assert.Contains("Total outstanding", figures);          // labelled as outstanding, never as revenue
            Assert.Contains("cash received", figures);               // recovered is the only cash figure
            Assert.Equal("Delinquent accounts", cut.Find("#arr-register-title").TextContent.Trim());
            // 7 in the financial report's position, 1 in the follow-up rows: stated, never forced to agree.
            Assert.Contains("Position: 7 delinquent rental accounts", cut.Find(".arr-scope-note").TextContent);
            Assert.Contains("Register: 1 accounts in the whole-account follow-up", cut.Find(".arr-scope-note").TextContent);
            Assert.NotNull(cut.Find("details.arr-note-disclosure summary"));
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
    public void SpaceRentalsJoinTheRegister_UnderTheirOwnCondition_OneRowPerAccount_AndCountInTheFigures()
    {
        ArrearsData();
        ObligationFollowUpItemDto Month(string payer, Guid account, DateOnly period, decimal owed, string reason, string? basis, bool earlier = false) =>
            new(account, Guid.Empty, payer, "4", new(FollowUpScopeKind.MonthlySpace, "VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable / Fruits"), period, owed, 0m, owed,
                "Normal", null, "/operations/vegetable-fruit", reason, basis, earlier, new DateOnly(2025, 10, 24));
        var greggor = Guid.NewGuid();
        var history = Result<FollowUpQueueDto>.Success(new FollowUpQueueDto("October 2026", DateOnly.FromDateTime(DateTime.Today),
            [new(1, "High", "Arrears", "arrears", FacilityCode.TCC, "Monthly rental", "Pedro Reyes", "Stall 4", 8_000m, false, "Nov 2025 – Dec 2025", "Arrears · 2 months · previous year", "Record payment", "/profile/tcc/4")],
            ObligationItems:
            [
                Month("Greggor Dal", greggor, new(2025, 11, 1), 10_000m, "arrears", "Expired contract"),
                Month("Greggor Dal", greggor, new(2025, 12, 1), 10_000m, "arrears", "Expired contract"),
                Month("Ana Cruz", Guid.NewGuid(), new(2026, 8, 1), 500m, "delinquent", null)
            ]));
        _reports.Setup(x => x.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(history);

        var cut = RenderComponent<Arrears>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Cruz", cut.Markup), Timeout);                       // Delinquent space month on the Delinquent register
        Assert.DoesNotContain("Greggor Dal", cut.Find("table").TextContent);
        Assert.Contains("8", cut.Find(".v3-hero, header").TextContent);                                    // 7 delinquent rentals + 1 space account

        cut.Find("select").Change("arrears");
        cut.WaitForAssertion(() =>
        {
            var row = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Greggor Dal"));
            Assert.Contains("₱20,000.00", row.TextContent);                                                // one row, both months
            Assert.Contains("Arrears · term ended Oct 24, 2025 · 2 months", row.TextContent);
            Assert.Contains("2 of 2 Arrears accounts shown", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void TheArrearsFilterListsTheServersArrearsRows_NotTheDelinquentOnesAndNotAnEmptyRegister()
    {
        ArrearsData(); var cut=RenderComponent<Arrears>();
        cut.WaitForAssertion(()=>Assert.Contains("Rosa Magbanua",cut.Markup),Timeout);
        cut.Find("select").Change("arrears");
        // The select's bound render can complete after Change returns when the
        // component also has background account-total lookups in flight.
        // Assert the final rendered filter, not the transient preceding frame.
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Qualifying Arrears", cut.Find("#arr-register-title").TextContent.Trim());
            Assert.Contains("Pedro Reyes", cut.Markup); // Arrears row, not Delinquent
            Assert.DoesNotContain("Rosa Magbanua", cut.Markup);
            Assert.DoesNotContain("Current Only", cut.Markup);
            Assert.Contains("1 of 1 Arrears accounts shown", cut.Markup);
        }, Timeout);
        SpaceHolderWorkspaceTests.SaveVisualArtifact("arrears-qualification-unavailable", cut.Markup);
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

        cut.OpenStatusReport();

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
