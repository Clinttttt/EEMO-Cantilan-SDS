using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The official financial tabs (IA-051, IA-052): Monthly Income in the office statement's rows, the Collections register with
/// a derived RCD-style summary, and Accountability with pesos kept apart from form counts. Every figure is the server's.
/// </summary>
public sealed class OfficialReportPanelsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficialReportsApiClient> _reports = new();
    private readonly Mock<IRemittancesApiClient> _remittances = new();

    public OfficialReportPanelsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(_reports.Object);
        Services.AddSingleton(_remittances.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private static IReadOnlyList<MonthlyIncomeCellDto> Months(decimal legacy, decimal canonical, int month = 9)
    {
        var cells = Enumerable.Range(1, 12).Select(_ => new MonthlyIncomeCellDto(0m, 0m)).ToList();
        cells[month - 1] = new MonthlyIncomeCellDto(legacy, canonical);
        return cells;
    }

    private static OfficialMonthlyIncomeRowDto Row(string key, string label, decimal legacy, decimal canonical, string authority) =>
        new(key, label, key, Months(legacy, canonical), new MonthlyIncomeCellDto(legacy, canonical), authority, null, null);

    private static OfficialMonthlyIncomeDto Statement(int? month)
    {
        var market = new[] { Row("MARKET_FEES", "Market Fees", 0m, 30m, "Canonical"), Row("ECF", "General Distribution / ECF", 100m, 0m, "Legacy") };
        var rent = new[] { Row("RENT_TCC", "Tampak Commercial Center (TCC)", 900m, 0m, "Legacy") };
        OfficialMonthlyIncomeGroupDto Group(string key, string label, params OfficialMonthlyIncomeRowDto[] rows) => new(key, label, rows,
            Months(rows.Sum(r => r.Total.Legacy), rows.Sum(r => r.Total.Canonical)),
            new MonthlyIncomeCellDto(rows.Sum(r => r.Total.Legacy), rows.Sum(r => r.Total.Canonical)));
        var groups = new[] { Group("MARKET", "Income from Market", market), Group("RENT", "Rent Income - Stall Rental", rent) };
        return new OfficialMonthlyIncomeDto(2026, month, groups, Months(1000m, 30m), new MonthlyIncomeCellDto(1000m, 30m), false,
            ["Each row counts a real collection once."], DateTime.UtcNow);
    }

    [Fact]
    public void MonthlyIncome_FollowsTheOfficeStatement_NotTheFacilities_AndNeverInventsATarget()
    {
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, 9)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement(9)));

        var cut = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));

        cut.WaitForAssertion(() =>
        {
            var table = cut.Find("table.mi-table");
            Assert.Contains("INCOME FROM MARKET", table.TextContent);
            Assert.Contains("RENT INCOME - STALL RENTAL", table.TextContent);
            var rows = table.QuerySelectorAll("tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Market Fees") && r.Contains("30.00") && r.Contains("Canonical"));
            Assert.Contains(rows, r => r.Contains("Tampak Commercial Center (TCC)") && r.Contains("900.00"));
            Assert.Contains("1,030.00", table.QuerySelector("tfoot")!.TextContent);
            // No annual target is configured, so none is shown and attainment is not fabricated.
            Assert.DoesNotContain("%", table.QuerySelector("tbody")!.TextContent);
            Assert.Contains("does not replace the office", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void TheLegacyCanonicalSplit_IsOptional_AndAnnualModeShowsEveryMonth()
    {
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, 9)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement(9)));
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement(null)));

        var monthly = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));
        monthly.WaitForAssertion(() => Assert.DoesNotContain("Canonical</th>", monthly.Markup), Timeout);
        monthly.Find("input[type='checkbox']").Change(true);
        monthly.WaitForAssertion(() =>
        {
            var headings = monthly.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("Legacy", headings);
            Assert.Contains("Canonical", headings);
        }, Timeout);

        var annual = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026));
        annual.WaitForAssertion(() =>
        {
            var headings = annual.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("Jan", headings);
            Assert.Contains("Dec", headings);
            Assert.Contains("Total", headings);
        }, Timeout);
    }

    [Fact]
    public void MonthlyIncome_FailedLoad_SaysSo_InsteadOfZeroes()
    {
        _reports.Setup(x => x.GetMonthlyIncomeAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Failure("offline"));

        var cut = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("table"));
        }, Timeout);
    }

    private static CollectionsRegisterDto Register()
    {
        var market = Guid.NewGuid();
        var ice = Guid.NewGuid();
        var a = Guid.NewGuid();
        return new CollectionsRegisterDto(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
        [
            new CollectionRegisterRowDto(a, new DateOnly(2026, 9, 3), "CT-000101", RevenueInstrumentType.CashTicket, null, Guid.NewGuid(), "Ana Reyes",
                market, "Market Fees", "Market Fees", 30m, 0m, "Posted"),
            new CollectionRegisterRowDto(Guid.NewGuid(), new DateOnly(2026, 9, 4), "OR-000201", RevenueInstrumentType.OfficialReceipt, "Lisa Reyes", null, null,
                ice, "Ice Plant", "ICE · Stall ICE-01 Sep 2026", 250m, 0m, "Posted"),
        ],
        [
            new CollectionsSummaryLineDto(market, "Market Fees", 1, 30m, 0m, 30m, "CT-000101", "CT-000101"),
            new CollectionsSummaryLineDto(ice, "Ice Plant", 1, 250m, 0m, 250m, "OR-000201", "OR-000201"),
        ], 280m, 0m, 280m, false);
    }

    [Fact]
    public void CollectionsRegister_ShowsTheServerSummaryAndRows_AndOpensTheItemizedDocument()
    {
        var register = Register();
        _reports.Setup(x => x.GetCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null))
            .ReturnsAsync(Result<CollectionsRegisterDto>.Success(register));
        var target = register.Rows[1];
        _reports.Setup(x => x.GetCollectionAsync(target.CollectionId)).ReturnsAsync(Result<CollectionDocumentDto>.Success(new CollectionDocumentDto(
            target.CollectionId, "OR-000201", RevenueInstrumentType.OfficialReceipt, "Issued / consumed", target.BusinessDate, DateTime.UtcNow,
            "Lisa Reyes", null, 250m, 0m, [new CollectionDocumentLineDto("Ice Plant", target.SourceLabel, 250m, 0m, ["ICE allocation: ₱250.00"])], null, null)));

        var cut = RenderComponent<CollectionsRegisterPanel>(p => p.Add(x => x.From, new DateOnly(2026, 9, 1)).Add(x => x.To, new DateOnly(2026, 9, 30)).Add(x => x.PeriodLabel, "September 2026"));

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("table[aria-label='Collections by revenue line']");
            Assert.Contains("Market Fees", summary.TextContent);
            Assert.Contains("₱280.00", summary.QuerySelector("tfoot")!.TextContent);
            Assert.Equal(2, cut.FindAll("[aria-label='Collections register'] tbody tr").Count);
            Assert.Contains("Lisa Reyes", cut.Markup);
            Assert.Contains("Office", cut.Markup);   // a Web collection has no collector
        }, Timeout);

        cut.FindAll("button.cr-link").Single(b => b.TextContent.Trim() == "OR-000201").Click();
        cut.WaitForAssertion(() =>
        {
            var doc = cut.Find("aside[aria-label='Collection document']");
            Assert.Contains("OR-000201", doc.TextContent);
            Assert.Contains("not yet remitted", doc.TextContent);
            Assert.Contains("ICE allocation", doc.TextContent);
        }, Timeout);
    }

    [Fact]
    public void Accountability_KeepsPesosApartFromFormCounts_AndTracesASerial()
    {
        var collector = Guid.NewGuid();
        _remittances.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<AccountabilityPositionDto>.Success(
            new AccountabilityPositionDto(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 20000m, 20000m, 0m, 0, 0m,
            [
                new CollectorPositionDto(collector, "Ana Reyes", 20000m, 20000m, 0m, 0, 0m,
                    [new FormAccountabilityDto(RevenueInstrumentType.CashTicket, 10000, 725, 0, 0, 0, 9275)])
            ])));
        var collectionId = Guid.NewGuid();
        _reports.Setup(x => x.TraceDocumentAsync("CT-004120")).ReturnsAsync(Result<DocumentTraceDto>.Success(new DocumentTraceDto(
            "CT-004120", RevenueInstrumentType.CashTicket, "Issued / consumed", "Ana Reyes", null,
            new CollectionDocumentDto(collectionId, "CT-004120", RevenueInstrumentType.CashTicket, "Issued / consumed", new DateOnly(2026, 9, 3),
                DateTime.UtcNow, null, "Ana Reyes", 30m, 0m, [new CollectionDocumentLineDto("Market Fees", "Market Fees", 30m, 0m, [])],
                Guid.NewGuid(), "Recorded"))));

        var cut = RenderComponent<AccountabilityPanel>(p => p.Add(x => x.From, new DateOnly(2026, 9, 1)).Add(x => x.To, new DateOnly(2026, 9, 30)).Add(x => x.PeriodLabel, "September 2026"));

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Collector accountability'] tbody tr"));
            Assert.Contains("10000", row.TextContent);
            Assert.Contains("9275", row.TextContent);
            Assert.Contains("₱20,000.00", row.TextContent);
            Assert.DoesNotContain("₱9,275", cut.Markup);   // remaining tickets are never money
            Assert.Contains("never an amount", cut.Markup);
        }, Timeout);

        cut.Find("form[aria-label='Trace a document'] input").Change("CT-004120");
        cut.Find("form[aria-label='Trace a document']").Submit();
        cut.WaitForAssertion(() =>
        {
            var trace = cut.Find("dl[aria-label='Document trace']").TextContent;
            Assert.Contains("Issued / consumed", trace);
            Assert.Contains("Market Fees ₱30.00", trace);
            Assert.Contains("Covered (Recorded)", trace);
        }, Timeout);
    }
}
