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
            // One authoritative amount per row; the legacy/canonical authority is a reconciliation detail, not shown by default.
            Assert.Contains(rows, r => r.Contains("Market Fees") && r.Contains("30.00") && !r.Contains("Canonical"));
            Assert.Contains(rows, r => r.Contains("Tampak Commercial Center (TCC)") && r.Contains("900.00"));
            Assert.Contains("1,030.00", table.QuerySelector("tfoot")!.TextContent);
            // No annual target is configured, so none is shown and attainment is not fabricated.
            Assert.DoesNotContain("%", table.QuerySelector("tbody")!.TextContent);
            // The statement carries no explanatory footer notes (V3 removed them); the figures stand on their own.
            Assert.Empty(cut.FindAll("ul.mi-notes"));
            Assert.DoesNotContain("does not replace the office", cut.Markup);
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
            // The reconciliation control is secondary: it lives in a collapsed "Reconciliation details" disclosure.
            Assert.NotNull(monthly.Find("details.mi-recon input[type='checkbox']"));
        }, Timeout);

        // Monthly mode is a focused statement: the selected month and year to date, not all twelve months.
        var monthlyHeadings = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));
        monthlyHeadings.WaitForAssertion(() =>
        {
            var headings = monthlyHeadings.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
            Assert.Contains("Sep", headings);
            Assert.Contains("YTD", headings);
            Assert.DoesNotContain("Jan", headings);
            Assert.Contains("Annual target", headings);
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
    public void AnItemizedOfficialReceipt_ReadsAsOneDocumentWithItsLines_NotSeveralReceipts()
    {
        var collection = Guid.NewGuid();
        var rent = Guid.NewGuid();
        var ecf = Guid.NewGuid();
        var register = new CollectionsRegisterDto(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
        [
            new CollectionRegisterRowDto(collection, new DateOnly(2026, 9, 5), "OR-000001", RevenueInstrumentType.OfficialReceipt, "Lisa Cruz", null, null,
                rent, "Stall Rental", "NPM · Stall 12", 500m, 0m, "Posted"),
            new CollectionRegisterRowDto(collection, new DateOnly(2026, 9, 5), "OR-000001", RevenueInstrumentType.OfficialReceipt, "Lisa Cruz", null, null,
                ecf, "General Distribution / ECF", "ECF · Stall 12", 650m, 0m, "Posted"),
        ],
        [
            new CollectionsSummaryLineDto(rent, "Stall Rental", 1, 500m, 0m, 500m, "OR-000001", "OR-000001"),
            new CollectionsSummaryLineDto(ecf, "General Distribution / ECF", 1, 650m, 0m, 650m, "OR-000001", "OR-000001"),
        ], 1150m, 0m, 1150m, false);
        _reports.Setup(x => x.GetCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null))
            .ReturnsAsync(Result<CollectionsRegisterDto>.Success(register));

        var cut = RenderComponent<CollectionsRegisterPanel>(p => p.Add(x => x.From, new DateOnly(2026, 9, 1)).Add(x => x.To, new DateOnly(2026, 9, 30)).Add(x => x.PeriodLabel, "September 2026"));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Collections register'] tbody tr");
            Assert.Equal(2, rows.Count);                                         // both revenue lines are listed...
            Assert.Single(cut.FindAll("button.cr-link"));                          // ...under one document link
            Assert.Contains("cr-cont", rows[1].GetAttribute("class"));
            Assert.Contains("General Distribution / ECF", rows[1].TextContent);
        }, Timeout);
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

    // ── Official Monthly Income: the final statement, in the office sheet's structure ──

    private IRenderedComponent<OfficialMonthlyIncome> RenderOfficial(int year)
    {
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/reports/monthly-income/official?year={year}");
        return RenderComponent<OfficialMonthlyIncome>();
    }

    [Fact]
    public void TheOfficialStatement_LettersTheOfficeLines_ShowsTheFullYear_AndNeverInventsATargetOrSubtotal()
    {
        var statement = Statement(null) with { Year = 2025 };
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(statement));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            var table = cut.Find("table.omi-table");
            var head = table.QuerySelectorAll("thead th").Select(th => th.TextContent.Trim()).ToList();
            Assert.Equal(16, head.Count);                                   // line, target, Jan–Dec, Total, Percentage
            Assert.Equal(["Annual target", "Jan"], head.Skip(1).Take(2));
            Assert.Equal(["Total", "Percentage"], head.TakeLast(2));
            Assert.Contains("1. Receipts", table.TextContent);
            Assert.Contains("A. Income from Market", table.TextContent);
            var lines = table.QuerySelectorAll("tbody th.omi-line").Select(th => th.TextContent.Trim()).ToList();
            Assert.Equal(["a. Market Fees", "b. General Distribution / ECF", "c. Tampak Commercial Center (TCC)"], lines);
            Assert.DoesNotContain("Subtotal", table.TextContent);
            Assert.Contains("Total Income Market Operation", table.QuerySelector("tfoot")!.TextContent);
            Assert.Contains("1,030.00", table.QuerySelector("tfoot")!.TextContent);
            // No target configured: target and percentage are a dash, never 0 or 0%.
            Assert.DoesNotContain("%", table.QuerySelector("tbody")!.TextContent);
            // The dashes speak for themselves: no explanatory notes or footer clutter on the official output.
            Assert.DoesNotContain("Annual targets are not configured", cut.Markup);
            Assert.Empty(cut.FindAll(".omi-notes"));
            // A past year has every month reached: a month with nothing collected is a known 0.00.
            Assert.Contains("0.00", table.QuerySelector("tbody")!.TextContent);
            // No analysis chrome: no report tabs and no source-performance widgets on the final output.
            Assert.Empty(cut.FindAll(".rpt-sec-tabs"));
            Assert.Empty(cut.FindAll(".rsp"));
        }, Timeout);
    }

    [Fact]
    public void TheOfficialStatement_FlagsCollectedMoneyWithoutAPlacement_AndListsItApart_NeverInAGroup()
    {
        var statement = Statement(null) with { Year = 2025 };
        var slaughter = Row("SLAUGHTERHOUSE", "Slaughterhouse", 200m, 0m, "Legacy");
        statement = statement with
        {
            Groups = statement.Groups.Append(new OfficialMonthlyIncomeGroupDto("PENDING", "Awaiting an approved official grouping",
                [slaughter], slaughter.Months, slaughter.Total)).ToList()
        };
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(statement));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Official report requires review", cut.Find(".omi-review").TextContent);
            var unplaced = cut.Find("tbody.omi-unplaced");
            Assert.Contains("Slaughterhouse", unplaced.TextContent);
            // Not lettered: a letter would claim a place on the office sheet.
            Assert.DoesNotContain("d. Slaughterhouse", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void TheOfficialStatement_ThatCannotBeRead_SaysSo_AndOffersNoPrint()
    {
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Failure("down"));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be prepared", cut.Markup);
            Assert.True(cut.Find(".omi-print").HasAttribute("disabled"));
            Assert.Empty(cut.FindAll("table.omi-table"));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheMonthlyIncomeWorkspace_LinksToTheOfficialStatement_ApartFromItsOwnPrint()
    {
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, 9)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement(9)));

        var cut = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));

        cut.WaitForAssertion(() =>
        {
            var link = cut.FindAll("a").Single(a => a.TextContent.Contains("Official Monthly Income"));
            Assert.Equal("/reports/monthly-income/official?year=2026", link.GetAttribute("href"));
            Assert.Contains("Print this view", cut.Markup);
        }, Timeout);
    }

    // ── Revenue source performance: every source, described by its own model ──

    private static RevenueSourcePerformanceRowDto Source(string key, string label, string group, string model, decimal collected,
        int? transactions, FacilityCode? facility = null, string? instruments = null) =>
        new(key, label, group, group, model, instruments, facility, collected, 0m, collected, transactions, transactions, transactions,
            collected != 0m ? "Active" : "Nothing recorded", false);

    [Fact]
    public void SourcePerformance_ListsOperationsBesideFacilities_AndNeverGivesAPaidOnServiceSourceARate()
    {
        var rows = new[]
        {
            Source("LANDING_BERTHING", "Landing / Berthing", "MARKET", RevenueSourceModel.Transactional, 100m, 1, instruments: "CT"),
            Source("RENT_TCC", "Tampak Commercial Center (TCC)", "RENT", RevenueSourceModel.RecurringObligation, 900m, null, FacilityCode.TCC, "OR"),
        };
        var dto = new RevenueSourcePerformanceDto(2026, 10,
            [new RevenueSourceGroupDto("MARKET", "Income from Market", 100m), new RevenueSourceGroupDto("RENT", "Rent / facility operations", 900m)],
            rows, 1000m, [], DateTime.UtcNow);
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(dto));
        var tcc = new EEMOCantilanSDS.Application.Dtos.Reports.FinancialFacilityRowDto(FacilityCode.TCC, "Tampak Commercial Center",
            "Monthly rental", false, 900m, 300m, 3, 4, 75, "Partial");

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.Facilities, new[] { tcc }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Revenue source performance — October 2026", cut.Find("#rsp-title").TextContent);
            var landing = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Landing / Berthing"));
            Assert.Contains("Paid on service · CT", landing.TextContent);
            Assert.Contains("₱100", landing.TextContent);
            Assert.Contains("1 transaction", landing.TextContent);
            Assert.DoesNotContain("%", landing.TextContent);
            Assert.DoesNotContain("unpaid", landing.TextContent);
            var rent = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Tampak Commercial Center"));
            Assert.Contains("₱300 unpaid · 3/4 paid · 75%", rent.TextContent);
            Assert.Contains("Outstanding balances", rent.TextContent);
            Assert.Contains("₱1,000", cut.Find("tr.rsp-total").TextContent);
        }, Timeout);

        // Expanding a transactional source states its activity and links to its own report.
        cut.FindAll("button.rsp-toggle").Single(b => b.TextContent.Contains("Landing / Berthing")).Click();
        cut.WaitForAssertion(() =>
        {
            var detail = cut.Find("tr.rsp-detail");
            Assert.Contains("Posted collections", detail.TextContent);
            Assert.Equal("/operations/landing-berthing/report", detail.QuerySelector("a.rsp-link")!.GetAttribute("href"));
        }, Timeout);
    }

    [Fact]
    public void SourcePerformance_DefaultsToAllSources_AndCanNarrowToFacilitiesWithoutReaddingMoney()
    {
        var rows = new[]
        {
            Source("LANDING_BERTHING", "Landing / Berthing", "MARKET", RevenueSourceModel.Transactional, 100m, 1, instruments: "CT"),
            Source("RENT_TCC", "Tampak Commercial Center (TCC)", "RENT", RevenueSourceModel.RecurringObligation, 900m, null, FacilityCode.TCC, "OR"),
        };
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(
            new RevenueSourcePerformanceDto(2026, 10,
                [new RevenueSourceGroupDto("MARKET", "Income from Market", 100m), new RevenueSourceGroupDto("RENT", "Rent / facility operations", 900m)],
                rows, 1000m, [], DateTime.UtcNow)));

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10));

        cut.WaitForAssertion(() => Assert.Contains("All sources", cut.Find(".rsp-filter").TextContent), Timeout);
        Assert.Equal(2, cut.FindAll("tr.rsp-row").Count);

        cut.Find(".rsp-filter .fh-dd-trigger").Click();
        cut.FindAll(".rsp-filter .fh-dd-item").Single(i => i.TextContent.Contains("Facilities only")).Click();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tr.rsp-row"));
            Assert.Contains("Tampak Commercial Center", row.TextContent);
            // A cross-group scope lists rows only; the server's totals are not re-added on the client.
            Assert.Contains("—", cut.Find("tr.rsp-total").TextContent);
        }, Timeout);
    }
}
