using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Collections;
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
    private readonly Mock<ICollectionActivityApiClient> _activity = new();

    public OfficialReportPanelsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(_reports.Object);
        Services.AddSingleton(_activity.Object);
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
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
    public void MonthlyIncome_renders_official_adjusted_cell_and_approved_target_from_server()
    {
        var cells = Enumerable.Range(1, 12).Select(_ => new MonthlyIncomeCellDto(0m, 0m)).ToArray();
        cells[8] = new(0m, 30m, 20m);
        var total = new MonthlyIncomeCellDto(0m, 30m, 20m);
        var row = new OfficialMonthlyIncomeRowDto("MARKET_FEES", "Market Fees", "MARKET_FEES", cells, total, "Canonical", 1000m, 5m);
        var report = new OfficialMonthlyIncomeDto(2026, 9, [new("MARKET", "Income from Market", [row], cells, total)],
            cells, total, true, [], DateTime.UtcNow);
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, 9)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(report));
        var cut = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));
        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("table.mi-table").TextContent;
            Assert.Contains("50.00", text);
            Assert.Contains("1,000.00", text);
        }, Timeout);
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
            new CollectionRegisterRowDto(a, new DateOnly(2026, 9, 3), "SRC-2026-000101", null, RevenueInstrumentType.CashTicket, null, Guid.NewGuid(), "Ana Reyes",
                market, "Market Fees", "Market Fees", 30m, 0m, "Posted"),
            new CollectionRegisterRowDto(Guid.NewGuid(), new DateOnly(2026, 9, 4), "SRC-2026-000201", null, RevenueInstrumentType.OfficialReceipt, "Lisa Reyes", null, null,
                ice, "Ice Plant", "ICE · Stall ICE-01 Sep 2026", 250m, 0m, "Posted"),
        ],
        [
            new CollectionsSummaryLineDto(market, "Market Fees", 1, 30m, 0m, 30m, "SRC-2026-000101", "SRC-2026-000101"),
            new CollectionsSummaryLineDto(ice, "Ice Plant", 1, 250m, 0m, 250m, "SRC-2026-000201", "SRC-2026-000201"),
        ], 280m, 0m, 280m, false);
    }

    private static CollectionsRegisterDto Itemized(Guid collection, string[] names, decimal[] amounts, bool truncated = false)
    {
        var rows = names.Select((n, i) => new CollectionRegisterRowDto(collection, new DateOnly(2026, 9, 5), "SRC-2026-000001", null, RevenueInstrumentType.OfficialReceipt,
            "Lisa Cruz", null, null, Guid.NewGuid(), n, n, amounts[i], 0m, "Posted")).ToList();
        return new CollectionsRegisterDto(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), rows,
            rows.Select(r => new CollectionsSummaryLineDto(r.ClassificationId, r.ClassificationName, 1, r.Amount, 0m, r.Amount, r.ReferenceCode, r.ReferenceCode)).ToList(),
            amounts.Sum(), 0m, amounts.Sum(), truncated);
    }

    private IRenderedComponent<CollectionsRegisterPanel> RenderCollections(CollectionsRegisterDto register)
    {
        _reports.Setup(x => x.GetCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null))
            .ReturnsAsync(Result<CollectionsRegisterDto>.Success(register));
        return RenderComponent<CollectionsRegisterPanel>(p => p.Add(x => x.From, new DateOnly(2026, 9, 1)).Add(x => x.To, new DateOnly(2026, 9, 30)).Add(x => x.PeriodLabel, "September 2026"));
    }

    [Fact]
    public void AnItemizedCollection_IsOneTransaction_WhoseAmountIsTheSumOfItsLines_AndTheSummaryIsUnchanged()
    {
        var cut = RenderCollections(Itemized(Guid.NewGuid(), ["Stall Rental", "General Distribution / ECF"], [500m, 650m]));

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Collections register'] tbody tr"));   // one SRC, one row
            Assert.Contains("Stall Rental · General Distribution / ECF", row.TextContent);          // a multi-line source is named, not collapsed to one line
            Assert.Contains("₱1,150.00", row.TextContent);
            Assert.Single(cut.FindAll("button.cr-link"));
            Assert.Equal(2, cut.FindAll("table[aria-label='Collections by revenue line'] tbody tr").Count);   // revenue-line totals still per classification
            Assert.Equal("Collection transactions", cut.Find(".cr-panel-head h3").TextContent.Trim());
        }, Timeout);
    }

    [Fact]
    public void ThreeSlaughterhouseLines_AppearOnceInTheRegister_WithTheFullAmount()
    {
        var cut = RenderCollections(Itemized(Guid.NewGuid(), ["Slaughterhouse", "Slaughterhouse", "Slaughterhouse"], [100m, 50m, 20m]));

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Collections register'] tbody tr"));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(row.TextContent, "Slaughterhouse").Count);
            Assert.Contains("₱170.00", row.TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheToolbarKeepsAccessibleNamesWithoutVisibleLabels_AndTheRegisterHasNoOperationColumn()
    {
        var cut = RenderCollections(Register());

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("select[aria-label='Instrument']"));
            Assert.NotNull(cut.Find("select[aria-label='Revenue line']"));
            Assert.Empty(cut.FindAll(".cr-tools .v3-field"));
            var headings = cut.FindAll("[aria-label='Collections register'] thead th").Select(h => h.TextContent.Trim()).ToList();
            Assert.DoesNotContain("Operation / source", headings);
            Assert.Contains("Revenue line", headings);
            Assert.DoesNotContain("Legacy-source", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void Search_FindsATransactionBySrcOrPayer_AndSaysWhenNothingMatches()
    {
        var cut = RenderCollections(Register());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[aria-label='Collections register'] tbody tr").Count), Timeout);

        cut.Find("input[aria-label='Search transactions']").Input("000201");
        Assert.Contains("SRC-2026-000201", Assert.Single(cut.FindAll("[aria-label='Collections register'] tbody tr")).TextContent);
        cut.Find("input[aria-label='Search transactions']").Input("ana");
        Assert.Contains("SRC-2026-000101", Assert.Single(cut.FindAll("[aria-label='Collections register'] tbody tr")).TextContent);
        cut.Find("input[aria-label='Search transactions']").Input("nobody");
        Assert.Contains("No transaction matches", cut.Find("[aria-label='Collections register'] tbody").TextContent);
    }

    [Fact]
    public void ATruncatedRegister_LeavesOutTheCollectionItMayHaveSplit_AndSaysSo()
    {
        var split = Guid.NewGuid();
        var whole = Itemized(Guid.NewGuid(), ["Market Fees"], [30m]);
        var cutRow = whole.Rows[0] with { CollectionId = split, ReferenceCode = "SRC-2026-000002", Amount = 40m };
        var cut = RenderCollections(whole with { Rows = [whole.Rows[0], cutRow], RowsTruncated = true });

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[aria-label='Collections register'] tbody tr"));
            Assert.Contains("search covers only these", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void CollectionsRegister_ShowsTheServerSummaryAndRows_AndOpensTheItemizedCollectionInAModal()
    {
        var register = Register();
        _reports.Setup(x => x.GetCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null))
            .ReturnsAsync(Result<CollectionsRegisterDto>.Success(register));
        var target = register.Rows[1];
        _reports.Setup(x => x.GetCollectionAsync(target.CollectionId)).ReturnsAsync(Result<CollectionDocumentDto>.Success(new CollectionDocumentDto(
            target.CollectionId, "SRC-2026-000201", null, RevenueInstrumentType.OfficialReceipt, null, target.BusinessDate, DateTime.UtcNow,
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

        Assert.Empty(cut.FindAll("[role='dialog']"));
        cut.FindAll("button.cr-link").Single(b => b.TextContent.Trim() == "SRC-2026-000201").Click();
        cut.WaitForAssertion(() =>
        {
            var doc = cut.Find("[role='dialog']");
            Assert.Contains("SRC-2026-000201", doc.TextContent);
            Assert.Contains("Not yet remitted", doc.TextContent);
            Assert.Contains("ICE allocation", doc.TextContent);
            Assert.Contains("₱250.00", doc.TextContent);
            Assert.Contains("Gross collection", doc.TextContent);
        }, Timeout);
        _reports.Verify(x => x.GetCollectionAsync(target.CollectionId), Times.Once);       // the clicked collection, not another

        cut.Find("[role='dialog']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role='dialog']"));
    }

    [Fact]
    public void TheCollectionModal_UsesTheRecordedChargeNames_OnlyWhenTheyAddBackToTheCollection()
    {
        var collection = Guid.NewGuid();
        var register = Itemized(collection, ["Slaughterhouse", "Slaughterhouse"], [120m, 50m]);
        _reports.Setup(x => x.GetCollectionAsync(collection)).ReturnsAsync(Result<CollectionDocumentDto>.Success(new CollectionDocumentDto(
            collection, "SRC-2026-000001", null, RevenueInstrumentType.OfficialReceipt, null, new DateOnly(2026, 9, 5), DateTime.UtcNow, "Lisa Cruz", "Ana Reyes", 170m, -20m,
            [new CollectionDocumentLineDto("Slaughterhouse", "Slaughterhouse", 120m, -20m, []), new CollectionDocumentLineDto("Slaughterhouse", "Slaughterhouse", 50m, 0m, [])], null, null)));
        ActivityEvent(collection, [new CollectionActivityLineDto("SLAUGHTERHOUSE", "Slaughterhouse", 120m, "GovernedService", null, null, null, null, "Slaughter fee · Hog", "Slaughterhouse"),
            new CollectionActivityLineDto("SLAUGHTERHOUSE", "Slaughterhouse", 50m, "GovernedService", null, null, null, null, "Inspection fee", "Slaughterhouse")], 170m);
        var cut = RenderCollections(register);
        cut.WaitForAssertion(() => cut.Find("button.cr-link"), Timeout);

        cut.Find("button.cr-link").Click();
        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[role='dialog']");
            Assert.Contains("Slaughter fee · Hog", dialog.TextContent);
            Assert.Contains("Inspection fee", dialog.TextContent);
            Assert.Contains("Ana Reyes", dialog.TextContent);
            Assert.Contains("₱170.00", dialog.TextContent);                               // gross
            Assert.Contains("₱150.00", dialog.TextContent);                               // net after the ₱20.00 correction
        }, Timeout);
        cut.Find("[role='dialog'] .cdm-foot button").Click();

        // Recorded charges that do not add back to the collection are not trusted: the report's own lines are shown instead.
        ActivityEvent(collection, [new CollectionActivityLineDto("SLAUGHTERHOUSE", "Slaughterhouse", 100m, "GovernedService", null, null, null, null, "Stray fee", "Slaughterhouse")], 100m);
        cut.Find("button.cr-link").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Stray fee", cut.Find("[role='dialog']").TextContent), Timeout);
    }

    private void ActivityEvent(Guid collection, IReadOnlyList<CollectionActivityLineDto> lines, decimal amount)
    {
        var e = new CollectionActivityEventDto("k", "Canonical", "Slaughterhouse", collection, new DateOnly(2026, 9, 5), DateTime.UtcNow, "SRC-2026-000001", null, null, [], [],
            "Lisa Cruz", null, null, "Ana Reyes", "Ana Reyes", null, null, null, amount, 0m, amount, "Posted", null, lines, []);
        _activity.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<FacilityCode?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 5), "Business date", "Net", DateTime.UtcNow, [e], 1, false, 0m, amount, 0m, amount, [])));
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
            new CollectionDocumentDto(collectionId, "SRC-2026-004120", "CT-004120", RevenueInstrumentType.CashTicket, "Issued / consumed", new DateOnly(2026, 9, 3),
                DateTime.UtcNow, null, "Ana Reyes", 30m, 0m, [new CollectionDocumentLineDto("Market Fees", "Market Fees", 30m, 0m, [])],
                Guid.NewGuid(), "Recorded"))));

        var cut = RenderComponent<AccountabilityPanel>(p => p.Add(x => x.From, new DateOnly(2026, 9, 1)).Add(x => x.To, new DateOnly(2026, 9, 30)).Add(x => x.PeriodLabel, "September 2026"));

        cut.WaitForAssertion(() =>
        {
            // Cash and forms are separate tables: pesos in one, counts in the other.
            var cash = Assert.Single(cut.FindAll("[aria-label='Collector accountability'] tbody tr"));
            Assert.Contains("₱20,000.00", cash.TextContent);
            Assert.DoesNotContain("10000", cash.TextContent);
            var forms = Assert.Single(cut.FindAll("[aria-label='Accountable forms'] tbody tr"));
            Assert.Contains("10000", forms.TextContent);
            Assert.Contains("9275", forms.TextContent);
            Assert.DoesNotContain("₱9,275", cut.Markup);   // remaining tickets are never money
            Assert.DoesNotContain("never an amount", cut.Markup);
            Assert.Equal("Trace an OR or SRC", cut.Find("label.ap-trace-label").TextContent.Trim());
            Assert.Equal("Enter OR number or SRC", cut.Find("form[aria-label='Trace a document'] input").GetAttribute("placeholder"));
        }, Timeout);

        cut.Find("form[aria-label='Trace a document'] input").Change("CT-004120");
        cut.Find("form[aria-label='Trace a document']").Submit();
        cut.WaitForAssertion(() =>
        {
            var trace = cut.Find("dl[aria-label='Document trace']").TextContent;
            Assert.Contains("Issued / consumed", trace);
            Assert.Contains("Covered (Recorded)", trace);
            var card = cut.Find(".ap-card").TextContent;
            Assert.Contains("Market Fees", card);
            Assert.Contains("₱30.00", card);
        }, Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accountability_TracesSRCThroughCollectionAndAuthoritativeRemittanceCoverage(bool covered)
    {
        var date = new DateOnly(2026, 10, 12);
        var collectionId = Guid.NewGuid();
        var remittanceId = covered ? Guid.NewGuid() : (Guid?)null;
        const string reference = "SRC-2026-000016";
        var activity = new CollectionActivityEventDto(
            EventKey: "canonical-one", Authority: "Canonical", Source: "ECF", CollectionId: collectionId,
            BusinessDate: date, RecordedAtUtc: DateTime.UtcNow, ReferenceCode: reference, DocumentNumber: null,
            InstrumentType: null, ReplacementDocumentNumbers: [], ReplacementReferenceCodes: [],
            PayerName: "Delta Rall", PayorId: null, CollectorId: null, CollectorName: "Personalunos",
            RecordedBy: "Personalunos", Facility: null, Subject: "Stall 2", LegacyStatus: null,
            Amount: 656m, CorrectionEffect: 0m, NetAmount: 656m, Disposition: "Posted",
            ReplacesCollectionId: null, Lines: [], Corrections: []);
        _activity.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<FacilityCode?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<int>(), reference))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new CollectionActivityFeedDto(
                date, date, "Business date", "LatestCorrected", DateTime.UtcNow, [activity],
                1, false, 0m, 656m, 0m, 656m, [])));
        _reports.Setup(x => x.GetCollectionAsync(collectionId))
            .ReturnsAsync(Result<CollectionDocumentDto>.Success(new CollectionDocumentDto(
                collectionId, reference, null, null, null, date, DateTime.UtcNow, "Delta Rall", "Personalunos",
                656m, 0m, [new CollectionDocumentLineDto("Electricity Consumption Fees", "ECF", 656m, 0m, [])],
                remittanceId, covered ? "Recorded" : null)));

        var cut = RenderComponent<AccountabilityPanel>(p => p
            .Add(x => x.From, new DateOnly(2026, 10, 1))
            .Add(x => x.To, new DateOnly(2026, 10, 31))
            .Add(x => x.PeriodLabel, "October 2026"));
        cut.Find("form[aria-label='Trace a document'] input").Change("src-2026-000016");
        cut.Find("form[aria-label='Trace a document']").Submit();
        cut.WaitForAssertion(() =>
        {
            var result = cut.Find("dl[aria-label='Collection reference trace']").TextContent;
            Assert.Contains(reference, result);
            Assert.Contains("Delta Rall", result);
            Assert.Contains("₱656.00", cut.Find(".ap-card").TextContent);
            Assert.Contains("No linked physical document", result);
            Assert.Contains(covered ? "Covered (Recorded)" : "Not yet covered by a remittance", result);
            Assert.Equal(covered ? 1 : 0, cut.FindAll("dl[aria-label='Collection reference trace'] a[href^='/remittances/']").Count);
            Assert.Empty(cut.FindAll("dl[aria-label='Document trace']"));
        }, Timeout);
        _reports.Verify(x => x.TraceDocumentAsync(It.IsAny<string>()), Times.Never);
        _reports.Verify(x => x.GetCollectionAsync(collectionId), Times.Once);
    }

    [Fact]
    public void Accountability_JoinsCollectorTotalsAndTable_WithoutTheOfficeOriginNotice()
    {
        _remittances.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Success(new AccountabilityPositionDto(
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 600m, 0m, 600m, 0, 0m, [], 1200m, 2)));
        var cut = RenderComponent<AccountabilityPanel>(p => p
            .Add(x => x.From, new DateOnly(2026, 10, 1)).Add(x => x.To, new DateOnly(2026, 10, 31)).Add(x => x.PeriodLabel, "October 2026"));

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find(".ap-cash-panel");
            var totals = panel.QuerySelector("dl[aria-label='Cash position']")!.TextContent;
            Assert.Contains("600.00", totals);
            Assert.DoesNotContain("1,200.00", totals);
            Assert.DoesNotContain("1,800.00", totals);
            Assert.NotNull(panel.QuerySelector("table"));
            Assert.Empty(cut.FindAll(".ap-office"));
            Assert.DoesNotContain("Office-origin cash", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void Accountability_ShowsNoOfficeOriginNoteWhenThereIsNone()
    {
        _remittances.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Success(new AccountabilityPositionDto(
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 0m, 0m, 0m, 0, 0m, [])));
        var cut = RenderComponent<AccountabilityPanel>(p => p
            .Add(x => x.From, new DateOnly(2026, 10, 1)).Add(x => x.To, new DateOnly(2026, 10, 31)).Add(x => x.PeriodLabel, "October 2026"));
        cut.WaitForAssertion(() => Assert.Contains("Unremitted", cut.Markup), Timeout);
        Assert.Empty(cut.FindAll("p.v3-notice"));
    }

    [Fact]
    public void Accountability_RejectsIncompleteSRCWithoutInventingADocumentOrRemittance()
    {
        _remittances.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Success(new AccountabilityPositionDto(
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 0m, 0m, 0m, 0, 0m, [])));
        var cut = RenderComponent<AccountabilityPanel>(p => p
            .Add(x => x.From, new DateOnly(2026, 10, 1))
            .Add(x => x.To, new DateOnly(2026, 10, 31))
            .Add(x => x.PeriodLabel, "October 2026"));
        cut.Find("form[aria-label='Trace a document'] input").Change("SRC-2026");
        cut.Find("form[aria-label='Trace a document']").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll("[role='alert']"), alert => alert.TextContent.Contains("Enter the full collection reference"));
            Assert.Empty(cut.FindAll("dl[aria-label='Collection reference trace']"));
        }, Timeout);
        _activity.Verify(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<FacilityCode?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
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
            Assert.Contains("A. Income From Market", table.TextContent);
            var lines = table.QuerySelectorAll("tbody th.omi-line").Select(th => th.TextContent.Trim()).ToList();
            Assert.Equal(["a. Market Fees", "b. General Distribution / ECF", "c. Tampak Commercial Center (TCC)"], lines);
            Assert.DoesNotContain("Subtotal", table.TextContent);
            var closing = cut.Find("[data-report-page='2'] tfoot");
            Assert.Contains("OVERALL TOTAL MARKET COLLECTION", closing.TextContent);
            Assert.Contains("1,030.00", closing.TextContent);
            // No target configured: target and percentage are a dash, never 0 or 0%.
            Assert.DoesNotContain("%", table.QuerySelector("tbody.omi-block")!.TextContent);
            // The dashes speak for themselves: no explanatory notes or footer clutter on the official output.
            Assert.DoesNotContain("Annual targets are not configured", cut.Markup);
            Assert.Empty(cut.FindAll(".omi-notes"));
            // A past year has every month reached: a month with nothing collected is a known 0.00.
            Assert.Contains("0.00", table.QuerySelector("tbody.omi-block")!.TextContent);
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
    public void TheMonthlyIncomeWorkspace_LinksToTheOfficialStatement_AndHasNoPrintOfItsOwn()
    {
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, 9)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement(9)));

        var cut = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));

        cut.WaitForAssertion(() =>
        {
            var link = cut.FindAll("a").Single(a => a.TextContent.Contains("Official Monthly Income"));
            Assert.Equal("/reports/monthly-income/official?year=2026", link.GetAttribute("href"));
            Assert.DoesNotContain("Print this view", cut.Markup);                                     // the formal statement is the one printable document
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Print"));
        }, Timeout);
    }

    [Fact]
    public void TheFormalStatement_NamesTheOfficeWithoutItsTrailingAcronym_ButStillOffersPrintAndSaveAsPdf()
    {
        Services.GetRequiredService<EEMOCantilanSDS.Client.Services.BrandingState>().Apply(new(Code: "CNT", TenantCode: "cantilan-sds", Name: "Cantilan", Province: "Surigao del Sur",
            OfficeName: "Municipal Economic Enterprises Development Office (MEEDO)", SealPath: null, Status: "Active", IsActive: true));
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(Statement(null) with { Year = 2025 }));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Municipal Economic Enterprises Development Office", cut.Find("p.omi-office").TextContent.Trim());
            Assert.DoesNotContain("(MEEDO)", cut.Find("header, .omi-office").ParentElement!.TextContent);
            Assert.Contains("Print / Save as PDF", cut.Find("button.omi-print").TextContent);
        }, Timeout);
        // The tenant's own setting is untouched: the shell can still say what the office calls itself.
        Assert.EndsWith("(MEEDO)", Services.GetRequiredService<EEMOCantilanSDS.Client.Services.BrandingState>().OfficeName);
    }

    // ── Revenue source performance: official money, annual targets and attention ──

    private static RevenueSourcePerformanceRowDto Source(string key, string label, string group, string model, decimal collected,
        int? transactions, FacilityCode? facility = null, string? instruments = null) =>
        new(key, label, group, group, model, instruments, facility, collected, 0m, collected, transactions, transactions, transactions,
            collected != 0m ? "Active" : "Nothing recorded", false);

    [Fact]
    public void SourcePerformance_OverviewLoadingSkeletonMatchesTheReadOnlyFiveColumnTable()
    {
        var pending = new TaskCompletionSource<Result<RevenueSourcePerformanceDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).Returns(() => pending.Task);
        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.OverviewMode, true));

        cut.WaitForAssertion(() =>
        {
            var table = cut.Find("table.rsp-skeleton-table");
            Assert.NotNull(cut.Find(".rsp-skeleton-head"));
            Assert.NotNull(cut.Find(".rsp-filter-skeleton"));
            Assert.Equal(5, table.QuerySelectorAll("thead th").Length);
            Assert.Single(table.QuerySelectorAll("tbody tr.rsp-skeleton-group"));
            Assert.Equal(5, table.QuerySelectorAll("tbody tr.rsp-skeleton-row").Length);
            Assert.Single(table.QuerySelectorAll("tfoot tr.rsp-skeleton-total"));
            Assert.Empty(cut.FindAll("button.rsp-target-btn, button.rsp-toggle, tr.rsp-detail, .fh-dd-trigger"));
            Assert.DoesNotContain("Basis", table.TextContent);
            Assert.DoesNotContain("Activity", table.TextContent);
            Assert.DoesNotContain("Position", table.TextContent);
            Assert.DoesNotContain("Status", table.TextContent);
            Assert.Equal("true", cut.Find("section.rsp").GetAttribute("aria-busy"));
        }, Timeout);

        var row = Source("MARKET_FEES", "Market Fees", "MARKET", RevenueSourceModel.Transactional, 20m, 1)
            with { AnnualTarget = 120m, Attainment = 16.7m };
        pending.SetResult(Result<RevenueSourcePerformanceDto>.Success(new RevenueSourcePerformanceDto(2026, 10,
            [new RevenueSourceGroupDto("MARKET", "Income from Market", 20m)], [row], 20m, [], DateTime.UtcNow)));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("table.rsp-table:not(.rsp-skeleton-table)"));
            Assert.Empty(cut.FindAll("button.rsp-target-btn"));
        }, Timeout);
    }

    [Fact]
    public void SourcePerformance_DefaultOverviewUsesFiveStaticManagementColumns()
    {
        var rows = new[]
        {
            Source("MARKET_FEES", "Market Fees", "MARKET", RevenueSourceModel.Transactional, 200m, 1, instruments: "CT")
                with { CollectorCount = 2, DocumentCount = 3, AnnualTarget = 1200m, Attainment = 16.7m },
            Source("TRANSPORTATION_PARKING", "Transportation Fees", "MARKET", RevenueSourceModel.Transactional, 40m, 3, instruments: "OR")
                with { CollectorCount = 2, DocumentCount = 3 },
        };
        var dto = new RevenueSourcePerformanceDto(2026, 10,
            [new RevenueSourceGroupDto("MARKET", "Income from Market", 240m), new RevenueSourceGroupDto("RENT", "Rent / facility operations", 0m)],
            rows, 240m, [], DateTime.UtcNow, new TargetCoverageDto(TargetCoverageState.Partial, 1, 20, 1200m, 200m, 16.7m));
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(dto));

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10).Add(x => x.OverviewMode, true));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Revenue source performance — October 2026", cut.Find("#rsp-title").TextContent);
            Assert.Equal(new[] { "Source", "Collected", "Annual target", "Contribution", "Attention" },
                cut.FindAll("table.rsp-table thead th").Select(h => h.TextContent.Trim()).ToArray());
            var market = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Market Fees"));
            Assert.Equal(5, market.QuerySelectorAll("th, td").Length);
            Assert.Contains("₱200", market.TextContent);
            Assert.Contains("83.3%", market.TextContent);
            Assert.Contains("₱1,200", market.QuerySelector("td.rsp-target")!.TextContent);
            Assert.Contains("16.7%", market.QuerySelector("td.rsp-target")!.TextContent);
            Assert.DoesNotContain("transaction", market.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("—", market.TextContent);
            var transport = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Transportation Fees"));
            Assert.Contains("Not set", transport.QuerySelector("td.rsp-target")!.TextContent);
            Assert.DoesNotContain("transaction", transport.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("16.7%", transport.TextContent);
            Assert.DoesNotContain("collector", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("document", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(cut.FindAll("button.rsp-target-btn"));
            Assert.DoesNotContain("OR", cut.Markup);
            Assert.DoesNotContain("CT", cut.Markup);
            Assert.DoesNotContain("Active", cut.Markup);
            Assert.DoesNotContain("Basis", cut.Find("table.rsp-table thead").TextContent);
            Assert.DoesNotContain("Position", cut.Find("table.rsp-table thead").TextContent);
            Assert.DoesNotContain("Status", cut.Find("table.rsp-table thead").TextContent);
            Assert.Empty(cut.FindAll(".rsp-cov"));
            Assert.DoesNotContain("Targets partially configured", cut.Markup);
            Assert.Empty(cut.FindAll("button.rsp-toggle"));
            Assert.Empty(cut.FindAll("tr.rsp-detail"));
            Assert.DoesNotContain("aria-expanded", cut.Find("table.rsp-table").OuterHtml);
            Assert.DoesNotContain("aria-controls", cut.Find("table.rsp-table").OuterHtml);
            Assert.Contains("₱240", cut.Find("tr.rsp-group").TextContent);
            Assert.Contains("₱240", cut.Find("tr.rsp-total").TextContent);
        }, Timeout);

        cut.Find(".rsp-filter .fh-dd-trigger").Click();
        var sourceMenu = cut.Find(".rsp-filter .fh-dd-menu");
        Assert.Contains("Non-facility operations", sourceMenu.TextContent);
        Assert.Contains("Rent / facility operations", sourceMenu.TextContent);
        Assert.Contains("fh-dd-menu-right", sourceMenu.ClassName);
    }

    [Fact]
    public void SourcePerformance_ZeroGroupDenominatorShowsDash()
    {
        var row = Source("WCF", "Water Consumption Fees / WCF", "MARKET", RevenueSourceModel.RecurringObligation, 0m, 0);
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(
            new RevenueSourcePerformanceDto(2026, 10, [new RevenueSourceGroupDto("MARKET", "Income from Market", 0m)], [row], 0m, [], DateTime.UtcNow)));

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10).Add(x => x.OverviewMode, true));

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("tr.rsp-row");
            Assert.Equal("—", row.QuerySelector("td.rsp-contribution")!.TextContent.Trim());
        }, Timeout);
    }

    [Fact]
    public void SourcePerformance_UsesOnlyRealRecurringBalancesForAttention()
    {
        var rows = new[]
        {
            Source("RENT_TCC", "Tampak Commercial Center (TCC)", "RENT", RevenueSourceModel.RecurringObligation, 300m, null, FacilityCode.TCC),
            Source("LANDING_BERTHING", "Landing / Berthing", "MARKET", RevenueSourceModel.Transactional, 20m, 1),
            Source("MARKET_FEES", "Market Fees", "MARKET", RevenueSourceModel.Transactional, 100m, null) with { LegacyCollected = 100m, CanonicalCollected = 0m },
            Source("PENALTIES_AND_FINES", "Fines", "OTHER", RevenueSourceModel.Transactional, 50m, 1) with { AwaitingPlacement = true },
        };
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(
            new RevenueSourcePerformanceDto(2026, 10,
                [new RevenueSourceGroupDto("MARKET", "Income from Market", 120m), new RevenueSourceGroupDto("RENT", "Rent / facility operations", 300m),
                    new RevenueSourceGroupDto("OTHER", "Other operations", 50m)], rows, 470m, [], DateTime.UtcNow)));
        var tcc = new EEMOCantilanSDS.Application.Dtos.Reports.FinancialFacilityRowDto(FacilityCode.TCC, "Tampak Commercial Center",
            "Monthly rental", false, 300m, 7800m, 18, 24, 75, "Partial");

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.Facilities, new[] { tcc }).Add(x => x.OverviewMode, true));

        cut.WaitForAssertion(() =>
        {
            var rent = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Tampak Commercial Center"));
            Assert.DoesNotContain("18 / 24 paid", rent.TextContent);
            Assert.Contains("₱7,800 outstanding", rent.TextContent);
            Assert.DoesNotContain("unpaid", rent.TextContent, StringComparison.OrdinalIgnoreCase);

            var service = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Landing / Berthing"));
            Assert.DoesNotContain("transaction", service.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("—", service.QuerySelector("td.rsp-attention")!.TextContent.Trim());

            var legacy = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Market Fees"));
            Assert.Contains("Not set", legacy.QuerySelector("td.rsp-target")!.TextContent);
            Assert.DoesNotContain("Legacy included", legacy.TextContent);
            Assert.Contains("Legacy data included", legacy.TextContent);
            Assert.DoesNotContain("0 transactions", legacy.TextContent);

            var placement = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Fines"));
            Assert.Contains("Needs official placement", placement.TextContent);
            Assert.Contains("warn", placement.QuerySelector("td.rsp-attention")!.ClassName);
        }, Timeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourcePerformance_ShowsIncomeFromTerminalAsItsOwnGroup_ApartFromTransportation_InOverviewAndReports(bool overview)
    {
        var rows = new[]
        {
            Source("TRANSPORTATION_PARKING", "Transportation / Parking", "MARKET", RevenueSourceModel.Transactional, 150m, 1),
            Source("TERMINAL_COMFORT_ROOM", "COMFORT ROOM", "TERMINAL", RevenueSourceModel.Transactional, 400m, 1),
            Source("TERMINAL_PULL_PUL_VANS_CARGO_VANS", "PULL PUL VANS, CARGO VANS", "TERMINAL", RevenueSourceModel.Transactional, 5800m, 1),
            Source("TERMINAL_TRICYCAD", "TRICYCAD", "TERMINAL", RevenueSourceModel.Transactional, 900m, 1),
            Source("SLAUGHTERHOUSE", "Slaughterhouse", "SLAUGHTERHOUSE", RevenueSourceModel.QuantityService, 200m, 1),
        };
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(
            new RevenueSourcePerformanceDto(2026, 10,
                [new RevenueSourceGroupDto("MARKET", "Income From Market", 150m), new RevenueSourceGroupDto("TERMINAL", "Income From Terminal", 7100m),
                    new RevenueSourceGroupDto("SLAUGHTERHOUSE", "Income From Slaughterhouse", 200m)], rows, 7450m, [], DateTime.UtcNow)));

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10).Add(x => x.OverviewMode, overview));

        cut.WaitForAssertion(() =>
        {
            var bands = cut.FindAll("tr.rsp-group").Select(r => r.TextContent).ToList();
            Assert.Contains(bands, b => b.Contains("Income From Terminal", StringComparison.OrdinalIgnoreCase) && b.Contains("₱7,100"));
            Assert.Contains(bands, b => b.Contains("Income From Slaughterhouse", StringComparison.OrdinalIgnoreCase));
            var terminalRows = cut.FindAll("tr.rsp-row").Where(r => new[] { "COMFORT ROOM", "PULL PUL VANS", "TRICYCAD" }.Any(n => r.TextContent.Contains(n))).ToList();
            Assert.Equal(3, terminalRows.Count);
            Assert.DoesNotContain(terminalRows, r => r.TextContent.Contains("Jeepney") || r.TextContent.Contains("Tricycle"));   // a vehicle class is never a report row
            var transport = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Transportation / Parking"));
            Assert.DoesNotContain("COMFORT", transport.TextContent);
        }, Timeout);
    }

    [Fact]
    public void SourcePerformance_NeverShowsOutstandingForAPaidOnServiceSource()
    {
        var row = Source("LANDING_BERTHING", "Landing / Berthing", "MARKET", RevenueSourceModel.Transactional, 100m, 1, FacilityCode.TCC);
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(
            new RevenueSourcePerformanceDto(2026, 10, [new RevenueSourceGroupDto("MARKET", "Income from Market", 100m)], [row], 100m, [], DateTime.UtcNow)));
        var facility = new EEMOCantilanSDS.Application.Dtos.Reports.FinancialFacilityRowDto(FacilityCode.TCC, "Tampak Commercial Center",
            "Monthly rental", false, 100m, 7800m, 1, 1, 100, "Partial");

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.Facilities, new[] { facility }).Add(x => x.OverviewMode, true));

        cut.WaitForAssertion(() =>
        {
            var source = cut.Find("tr.rsp-row");
            Assert.DoesNotContain("transaction", source.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("—", source.QuerySelector("td.rsp-attention")!.TextContent.Trim());
            Assert.DoesNotContain("outstanding", source.TextContent, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
    }

    [Fact]
    public void SourcePerformance_FilterKeepsTheServerGroupDenominatorAndNoCrossGroupSubtotal()
    {
        var rows = new[]
        {
            Source("ECF", "General Distribution / ECF", "MARKET", RevenueSourceModel.RecurringObligation, 100m, 1),
            Source("ICE_PLANT", "Ice Plant", "MARKET", RevenueSourceModel.RecurringObligation, 900m, 1, FacilityCode.ICE),
        };
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(
            new RevenueSourcePerformanceDto(2026, 10, [new RevenueSourceGroupDto("MARKET", "Income from Market", 1000m)], rows, 1000m, [], DateTime.UtcNow)));

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10).Add(x => x.OverviewMode, true));

        cut.WaitForAssertion(() => Assert.Contains("All sources", cut.Find(".rsp-filter").TextContent), Timeout);
        Assert.Equal(2, cut.FindAll("tr.rsp-row").Count);
        cut.Find(".rsp-filter .fh-dd-trigger").Click();
        cut.FindAll(".rsp-filter .fh-dd-item").Single(i => i.TextContent.Contains("Facilities only")).Click();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tr.rsp-row"));
            Assert.Contains("Ice Plant", row.TextContent);
            Assert.Equal("90.0%", row.QuerySelector("td.rsp-contribution")!.TextContent.Trim());
            Assert.Empty(cut.FindAll("tr.rsp-group td.rsp-num:not(:empty)"));
            Assert.Contains("—", cut.Find("tr.rsp-total").TextContent);
        }, Timeout);
    }

    [Fact]
    public void SourcePerformance_OverviewAndReportsShareColumnsValuesAndAttention_OnlyReportsEditsTargets()
    {
        var rows = new[]
        {
            Source("MARKET_FEES", "Market Fees", "MARKET", RevenueSourceModel.Transactional, 200m, 1)
                with { AnnualTarget = 1200m, Attainment = 16.7m },
            Source("TRANSPORTATION_PARKING", "Transportation Fees", "MARKET", RevenueSourceModel.Transactional, 40m, 3)
                with { AnnualTarget = 600m, Attainment = 6.7m },
            Source("RENT_TCC", "Tampak Commercial Center (TCC)", "RENT", RevenueSourceModel.RecurringObligation, 300m, null, FacilityCode.TCC)
                with { AnnualTarget = 3000m, Attainment = 10m },
            Source("PENALTIES_AND_FINES", "Fines", "OTHER", RevenueSourceModel.Transactional, 20m, 1)
                with { AwaitingPlacement = true },
        };
        var dto = new RevenueSourcePerformanceDto(2026, 10,
            [new RevenueSourceGroupDto("MARKET", "Income from Market", 240m),
                new RevenueSourceGroupDto("RENT", "Rent / facility operations", 300m),
                new RevenueSourceGroupDto("OTHER", "Other operations", 20m)],
            rows, 560m, [], DateTime.UtcNow, new TargetCoverageDto(TargetCoverageState.Partial, 3, 4, 4800m, 540m, 11.25m));
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(dto));
        var tcc = new EEMOCantilanSDS.Application.Dtos.Reports.FinancialFacilityRowDto(FacilityCode.TCC, "Tampak Commercial Center",
            "Monthly rental", false, 300m, 7800m, 18, 24, 75, "Partial");
        var headers = new[] { "Source", "Collected", "Annual target", "Contribution", "Attention" };

        var report = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.Facilities, new[] { tcc }));
        var overview = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.Facilities, new[] { tcc }).Add(x => x.OverviewMode, true));

        report.WaitForAssertion(() =>
        {
            Assert.Equal(headers, report.FindAll("table.rsp-table thead th").Select(h => h.TextContent.Trim()).ToArray());
            Assert.Equal(headers, overview.FindAll("table.rsp-table thead th").Select(h => h.TextContent.Trim()).ToArray());
            Assert.Contains("Revenue source performance — October 2026", report.Find("#rsp-title").TextContent);
            Assert.Equal("Actual collections and operating activity by revenue source. Target attainment uses adjusted official income.", report.Find(".rsp-sub").TextContent.Trim());
            Assert.DoesNotContain("Targets partially configured", report.Markup);
            Assert.DoesNotContain("Targets partially configured", overview.Markup);
            Assert.Empty(report.FindAll(".rsp-cov"));
            Assert.Empty(report.FindAll("button.rsp-toggle, tr.rsp-detail"));
            Assert.DoesNotContain("Basis", report.Find("table.rsp-table thead").TextContent);
            Assert.DoesNotContain("Activity", report.Find("table.rsp-table thead").TextContent);
            Assert.DoesNotContain("Position", report.Find("table.rsp-table thead").TextContent);
            Assert.DoesNotContain("Status", report.Find("table.rsp-table thead").TextContent);
            Assert.DoesNotContain("aria-expanded", report.Find("table.rsp-table").OuterHtml);

            var reportMarket = report.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Market Fees"));
            var overviewMarket = overview.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Market Fees"));
            Assert.Equal(reportMarket.QuerySelector("td.rsp-target")!.TextContent, overviewMarket.QuerySelector("td.rsp-target")!.TextContent);
            Assert.Contains("₱1,200", reportMarket.QuerySelector("td.rsp-target")!.TextContent);
            Assert.Contains("16.7%", reportMarket.QuerySelector("td.rsp-target")!.TextContent);
            Assert.Equal("83.3%", reportMarket.QuerySelector("td.rsp-contribution")!.TextContent.Trim());
            Assert.Equal(reportMarket.QuerySelector("td.rsp-contribution")!.TextContent, overviewMarket.QuerySelector("td.rsp-contribution")!.TextContent);
            Assert.Contains("—", reportMarket.QuerySelector("td.rsp-attention")!.TextContent);
            Assert.Equal(reportMarket.QuerySelector("td.rsp-attention")!.TextContent, overviewMarket.QuerySelector("td.rsp-attention")!.TextContent);

            var reportRent = report.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Tampak Commercial Center"));
            var overviewRent = overview.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Tampak Commercial Center"));
            Assert.Equal("₱7,800 outstanding", reportRent.QuerySelector("td.rsp-attention")!.TextContent.Trim());
            Assert.Equal(reportRent.QuerySelector("td.rsp-attention")!.TextContent, overviewRent.QuerySelector("td.rsp-attention")!.TextContent);
            Assert.NotNull(reportMarket.QuerySelector("button.rsp-target-btn"));
            Assert.Null(overviewMarket.QuerySelector("button.rsp-target-btn"));
            Assert.Empty(overview.FindAll("button.rsp-target-btn"));
        }, Timeout);

        report.Find(".rsp-filter .fh-dd-trigger").Click();
        var menu = report.Find(".rsp-filter .fh-dd-menu");
        Assert.Contains("fh-dd-menu-right", menu.ClassName);
        Assert.Contains("Facilities only", menu.TextContent);
        report.FindAll(".rsp-filter .fh-dd-item").Single(item => item.TextContent.Contains("Facilities only")).Click();
        report.WaitForAssertion(() =>
        {
            var onlyFacility = Assert.Single(report.FindAll("tr.rsp-row"));
            Assert.Contains("Tampak Commercial Center", onlyFacility.TextContent);
            Assert.Equal("100.0%", onlyFacility.QuerySelector("td.rsp-contribution")!.TextContent.Trim());
        }, Timeout);
    }

    private static ReportRevisionDto Revision(EEMOCantilanSDS.Domain.Entities.Revenue.OfficialReportRevisionKind kind, string key, decimal amount) =>
        new(Guid.NewGuid(), kind, key, 2026, 9, 1, null, amount, 30m, "Reason", null, null, Guid.NewGuid(), "Head", DateTime.UtcNow);

    [Fact]
    public void TheHead_AdjustsAnOfficialCell_WithAReason_AndConflictsReadAsOfficeWording()
    {
        var cells = Enumerable.Range(1, 12).Select(_ => new MonthlyIncomeCellDto(0m, 0m)).ToArray();
        cells[8] = new(0m, 30m);
        var total = new MonthlyIncomeCellDto(0m, 30m);
        var row = new OfficialMonthlyIncomeRowDto("MARKET_FEES", "Market Fees", "MARKET_FEES", cells, total, "Canonical", null, null);
        var report = new OfficialMonthlyIncomeDto(2026, 9, [new("MARKET", "Income from Market", [row], cells, total)], cells, total, false, [], DateTime.UtcNow);
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2026, 9)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(report));
        _reports.SetupSequence(x => x.AdjustMonthlyIncomeAsync(It.IsAny<SetMonthlyIncomeAdjustmentRequest>()))
            .ReturnsAsync(Result<ReportRevisionDto>.Failure("SystemAmountChanged", ResultStatus.Conflict))
            .ReturnsAsync(Result<ReportRevisionDto>.Success(Revision(EEMOCantilanSDS.Domain.Entities.Revenue.OfficialReportRevisionKind.MonthlyAdjustment, "MARKET_FEES", 20m)));

        var cut = RenderComponent<OfficialMonthlyIncomePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 9));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("button.mi-adjust")), Timeout);
        cut.Find("button.mi-adjust").Click();

        var form = cut.Find("form[aria-label='Adjust official amount']");
        Assert.Contains("₱30.00", cut.Markup);                                         // the system amount is shown, read only
        cut.Find("form input[type='number']").Input("50");
        Assert.Contains("+₱20.00", cut.Markup);                                        // the difference preview
        cut.Find("form input[maxlength='500']").Input("Corrected per voucher");
        form.Submit();

        cut.WaitForAssertion(() => Assert.Contains("system amount changed", cut.Find(".avm-error").TextContent), Timeout);
        Assert.DoesNotContain("SystemAmountChanged", cut.Markup);                       // machine codes never reach the office

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("form[aria-label='Adjust official amount']")), Timeout);
        var sent = _reports.Invocations.Where(i => i.Method.Name == "AdjustMonthlyIncomeAsync").Select(i => (SetMonthlyIncomeAdjustmentRequest)i.Arguments[0]).Last();
        Assert.Equal(("MARKET_FEES", 2026, 9, 30m, 50m, "Corrected per voucher", null), (sent.RowKey, sent.Year, sent.Month,
            sent.ExpectedSystemAmount, sent.OfficialAmount, sent.Reason, sent.ExpectedRevisionId));
    }

    [Fact]
    public void AnnualTargets_AreSetOnASourceRow_AndPartialCoverageIsNeverPresentedAsComplete()
    {
        var rows = new[]
        {
            Source("MARKET_FEES", "Market Fees", "MARKET", RevenueSourceModel.Transactional, 100m, 1, instruments: "CT") with { AnnualTarget = 1000m, Attainment = 45.5m },
            Source("ECF", "General Distribution / ECF", "MARKET", RevenueSourceModel.Transactional, 50m, 1, instruments: "OR"),
        };
        var dto = new RevenueSourcePerformanceDto(2026, 10, [new RevenueSourceGroupDto("MARKET", "Income from Market", 150m)], rows, 150m, [],
            DateTime.UtcNow, new TargetCoverageDto(TargetCoverageState.Partial, 1, 2, 1000m, 100m, 10m));
        _reports.Setup(x => x.GetSourcePerformanceAsync(2026, 10)).ReturnsAsync(Result<RevenueSourcePerformanceDto>.Success(dto));
        _reports.Setup(x => x.GetGovernanceAsync(2026)).ReturnsAsync(Result<IReadOnlyList<ReportRevisionDto>>.Success([]));
        SetAnnualTargetRequest? sent = null;
        _reports.Setup(x => x.SetTargetAsync(It.IsAny<SetAnnualTargetRequest>()))
            .Callback<SetAnnualTargetRequest>(r => sent = r)
            .ReturnsAsync(Result<ReportRevisionDto>.Success(Revision(EEMOCantilanSDS.Domain.Entities.Revenue.OfficialReportRevisionKind.AnnualTarget, "ECF", 500m)));
        TargetCoverageDto? reported = null;

        var cut = RenderComponent<RevenueSourcePerformancePanel>(p => p.Add(x => x.Year, 2026).Add(x => x.Month, 10)
            .Add(x => x.OnCoverage, (TargetCoverageDto? c) => reported = c));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Targets partially configured", cut.Markup);
            Assert.Empty(cut.FindAll(".rsp-cov"));
            var market = cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("Market Fees"));
            Assert.Contains("₱1,000", market.TextContent);
            Assert.Contains("45.5%", market.TextContent);
            Assert.Contains("Not set", cut.FindAll("tr.rsp-row").Single(r => r.TextContent.Contains("ECF")).TextContent);
            Assert.Equal(TargetCoverageState.Partial, reported?.State);
        }, Timeout);

        cut.Find("button[aria-label='Set annual target for General Distribution / ECF']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form[aria-label='Annual target']")), Timeout);
        cut.Find("form input[type='number']").Change("500");
        cut.FindAll("form input[maxlength='200']").Single().Change("Sangguniang Bayan Res. 12");
        cut.Find("form[aria-label='Annual target']").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal(("ECF", 2026, 500m, "Sangguniang Bayan Res. 12", null), (sent!.RowKey, sent.Year, sent.Amount, sent.ApprovedSource, sent.ExpectedRevisionId));
    }

    [Fact]
    public void TheCashSummaryKeepsAnnualTargetsInRevenuePerformance_NotCashPosition()
    {
        TargetCoverageDto Coverage(TargetCoverageState state, decimal? attainment) => new(state, 1, 2, 1000m, 100m, attainment);
        string Text(TargetCoverageDto? coverage) => RenderComponent<SummaryCashPosition>(p => p
            .Add(x => x.Collected, 1m).Add(x => x.From, new DateOnly(2026, 1, 1)).Add(x => x.To, new DateOnly(2026, 10, 1))
            .Add(x => x.TargetCoverage, coverage)).Markup;

        // Target coverage moved to Revenue Source Performance. The cash position must
        // not imply incomplete targets are zero, or confuse targets with collectible cash.
        foreach (var markup in new[] { Text(null), Text(Coverage(TargetCoverageState.Partial, 10m)), Text(Coverage(TargetCoverageState.Complete, 62.5m)) })
        {
            Assert.Contains("Collections and accountability position", markup);
            Assert.DoesNotContain("Not configured", markup);
            Assert.DoesNotContain("1 of 2 configured", markup);
            Assert.DoesNotContain("62.5%", markup);
            Assert.DoesNotContain("Target attainment", markup);
        }
    }

    [Fact]
    public void TheOfficialStatement_ShowsEveryServerFamily_NamesSlaughterhouseSectionC_AndCloseswithTheSignatories()
    {
        var statement = Statement(null) with { Year = 2025 };
        var terminal = new OfficialMonthlyIncomeGroupDto("TERMINAL", "B. Income From Terminal",
            [Row("COMFORT_ROOM", "COMFORT ROOM", 0m, 40m, "Canonical")], Months(0m, 40m), new MonthlyIncomeCellDto(0m, 40m));
        var slaughter = new OfficialMonthlyIncomeGroupDto("SLAUGHTERHOUSE", "Slaughterhouse",
            [Row("SLAUGHTERHOUSE", "Slaughterhouse", 200m, 0m, "Legacy")], Months(200m, 0m), new MonthlyIncomeCellDto(200m, 0m));
        var groups = statement.Groups.Append(terminal).Append(slaughter).ToList();
        OfficialMonthlyIncomeSectionDto Section(string key, string label, params string[] keys) => new(key, label, keys,
            key == "B" ? terminal.MonthTotals : Months(0m, 0m), key == "B" ? terminal.Total : new MonthlyIncomeCellDto(0m, 0m));
        statement = statement with { Groups = groups, Sections = [Section("A", "Income From Market", "MARKET", "RENT"), Section("B", "Income From Terminal", "TERMINAL"), Section("C", "Income from Slaughterhouse", "SLAUGHTERHOUSE")] };
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(statement));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("article.omi-sheet").Count);
            var first = cut.Find("[data-report-page='1']");
            var continuation = cut.Find("[data-report-page='2']");
            var table = continuation.QuerySelector("table.omi-table")!;
            Assert.Contains("A. Income From Market", first.TextContent);
            Assert.DoesNotContain("B. Income From Terminal", first.TextContent);
            Assert.DoesNotContain("C. Income from Slaughterhouse", first.TextContent);
            Assert.Empty(first.QuerySelectorAll("tfoot, .omi-sign"));
            Assert.Single(first.QuerySelectorAll(".omi-head"));
            Assert.Empty(continuation.QuerySelectorAll(".omi-head"));
            Assert.Contains("B. Income From Terminal", table.TextContent);          // the server's own section, never a client guess
            Assert.Contains("Total Income from Terminal", table.TextContent);
            Assert.Contains("COMFORT ROOM", table.TextContent);
            Assert.Contains("C. Income from Slaughterhouse", table.TextContent);
            Assert.Contains("OVERALL TOTAL MARKET COLLECTION", continuation.TextContent);
            Assert.Single(continuation.QuerySelectorAll("footer.omi-sign"));
            Assert.Equal(first.QuerySelector("colgroup")!.InnerHtml, table.QuerySelector("colgroup")!.InnerHtml);
            Assert.Equal(16, table.QuerySelectorAll("thead th").Length);
            Assert.Equal(groups.Sum(g => g.Rows.Count), cut.FindAll("tbody.omi-block tr:not(.omi-subtotal) > th.omi-line").Count);
            foreach (var row in groups.SelectMany(g => g.Rows))
                Assert.Single(cut.FindAll("tbody.omi-block tr"), r => r.QuerySelector("th.omi-line")?.TextContent.EndsWith(row.Label) == true);
            Assert.Contains("40.00", continuation.QuerySelector(".omi-subtotal")!.TextContent);
            Assert.DoesNotContain("Awaiting an approved official grouping", cut.Markup);
            Assert.Contains("Prepared by", cut.Find("footer.omi-sign").TextContent);         // the two official roles come from office settings
            Assert.Contains("Certified Correct", cut.Find("footer.omi-sign").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheOfficialStatement_PrintsTheConfiguredPreparedByAndCertifiedCorrect_WithTheirTitles()
    {
        var statement = Statement(null) with
        {
            Year = 2025,
            Signatories = [new("Prepared by", "A. Aide", "Admin. Aide III"), new("Certified Correct", "M. Supervisor", "Market Supervisor IV")]
        };
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(statement));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            var footer = cut.Find("footer.omi-sign");
            var foot = footer.TextContent;
            Assert.Contains("A. Aide", foot); Assert.Contains("Admin. Aide III", foot);
            Assert.Contains("M. Supervisor", foot); Assert.Contains("Market Supervisor IV", foot);
            Assert.Equal(["Prepared by:", "Certified Correct:"], footer.QuerySelectorAll(".osg-caption").Select(x => x.TextContent).ToArray());
            Assert.Equal(2, footer.QuerySelectorAll(".osg-signature-space").Length);                 // open signing space, as on the office paper
            Assert.Empty(footer.QuerySelectorAll(".osg-rule"));                                     // the office paper has no synthetic signature line
            Assert.Empty(cut.FindAll(".osg-setup"));                                                // nothing to set up
        }, Timeout);
    }

    [Fact]
    public void WithNoConfiguredSignatories_TheTwoRolesStayBlank_NeverInventedNames_AndTheHeadIsOfferedTheSetup()
    {
        var statement = Statement(null) with { Year = 2025, Signatories = [] };
        _reports.Setup(x => x.GetMonthlyIncomeAsync(2025, null)).ReturnsAsync(Result<OfficialMonthlyIncomeDto>.Success(statement));

        var cut = RenderOfficial(2025);

        cut.WaitForAssertion(() =>
        {
            var foot = cut.Find("footer.omi-sign").TextContent;
            Assert.Contains("Prepared by", foot); Assert.Contains("Certified Correct", foot);
            Assert.DoesNotContain("GANANCIAS", foot.ToUpperInvariant()); Assert.DoesNotContain("GUAZON", foot.ToUpperInvariant());
            Assert.Equal("/settings/report-signatories", cut.Find(".osg-setup a").GetAttribute("href"));
        }, Timeout);
    }
}
