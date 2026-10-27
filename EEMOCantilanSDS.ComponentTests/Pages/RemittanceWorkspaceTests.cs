using Bunit;
using EEMOCantilanSDS.Application.Dtos.Collections;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Remittance and liquidation (IA-052 / IA-062): money already collected and turned over, over canonical Collections. The
/// workspace derives what is expected from posted collections, keeps a shortfall visible, never lets the remitted amount
/// exceed it, shows no physical-form inventory or custody, and records nothing that looks like revenue.
/// </summary>
public sealed class RemittanceWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid CollectorId = Guid.NewGuid();
    private static readonly Guid CollectionA = Guid.NewGuid();
    private static readonly Guid CollectionB = Guid.NewGuid();
    private readonly Mock<IRemittancesApiClient> _api = new();

    public RemittanceWorkspaceTests()
    {
        var collectors = new Mock<ICollectorsApiClient>();
        collectors.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[]
        {
            new CollectorListDto(CollectorId, "Ana Reyes", "ana@example.test", "C-001", [], 0m, 0, null, true)
        }));
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(collectors.Object);
        Services.AddSingleton(_api.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        ServeHistory();
    }

    private void ServeHistory(params RemittanceHistoryRowDto[] rows) =>
        _api.Setup(x => x.GetHistoryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(),
                It.IsAny<RevenueInstrumentType?>(), It.IsAny<RemittanceStatus?>()))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceHistoryRowDto>>.Success(rows));

    private static RemittanceHistoryMemberDto Member(string name, decimal expected = 100m, decimal remitted = 100m,
        RemittanceStatus status = RemittanceStatus.Recorded) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, 2, expected, remitted, expected - remitted, status);

    private static RemittanceHistoryRowDto HistoryRow(string status = "Recorded", params RemittanceHistoryMemberDto[] members) => new(
        members.Length > 1 ? Guid.NewGuid() : null, members[0].RemittanceId, new DateOnly(2026, 10, 2), null,
        members.Sum(x => x.CollectionCount), members.Sum(x => x.ExpectedAmount), members.Sum(x => x.RemittedAmount),
        members.Sum(x => x.DifferenceAmount), status, DateTime.UtcNow, null, members);

    private static AccountabilityPositionDto Position(decimal collected = 20000m, decimal remitted = 20000m, int onHand = 9275) => new(
        new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), collected, remitted, collected - remitted, 0, 0m,
        [
            new CollectorPositionDto(CollectorId, "Ana Reyes", collected, remitted, collected - remitted, 0, 0m,
            [
                new FormAccountabilityDto(RevenueInstrumentType.CashTicket, 10000, 725, 0, 0, 0, onHand)
            ])
        ]);

    private void ServePosition(AccountabilityPositionDto position) =>
        _api.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Success(position));

    private static RemittanceScopeDto Scope() => new(CollectorId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null,
        [
            new RemittanceCollectionDto(CollectionA, new DateOnly(2026, 9, 3), "SRC-2026-000101", RevenueInstrumentType.CashTicket, "Walk-up", 30m,
                [new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 30m)]),
            new RemittanceCollectionDto(CollectionB, new DateOnly(2026, 9, 4), "SRC-2026-000102", RevenueInstrumentType.CashTicket, null, 50m,
                [new RemittanceBreakdownDto(Guid.NewGuid(), "Landing/Berthing", 50m)]),
        ], 80m,
        [new RemittanceBreakdownDto(Guid.NewGuid(), "Landing/Berthing", 50m), new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 30m)],
        "SRC-2026-000101", "SRC-2026-000102");

    private static void OpenTab(IRenderedComponent<Remittances> cut, string name) =>
        cut.FindAll("button.rm-tab").Single(b => b.TextContent.Trim() == name).Click();

    [Fact]
    public void EmptyRegistersKeepTheirPanelAcrossAllTabs_AndPrintIsInTheToolbar()
    {
        ServePosition(Position(0m, 0m) with { Collectors = [] });
        var cut = RenderComponent<Remittances>();
        cut.WaitForAssertion(() => Assert.Contains("No SRC-backed collector Collections ready to remit.", cut.Markup), Timeout);
        Assert.Contains("Legacy payment records require separate reconciliation", cut.Markup);
        Assert.Single(cut.FindAll(".rm-workspace"));
        Assert.Empty(cut.FindAll(".v3h .rm-ctl"));
        Assert.Equal("Print", cut.Find(".rm-nav .rm-ctl button").TextContent.Trim());
        foreach (var tab in new[] { "For remittance", "Collector position", "Remittance history" })
        {
            OpenTab(cut, tab);
            var panel = Assert.Single(cut.FindAll(".rm-tab-panel"));
            Assert.Equal(tab, panel.QuerySelector("h2")!.TextContent.Trim());
            Assert.NotNull(panel.QuerySelector(".v3-table-wrap table thead"));
            Assert.NotNull(panel.QuerySelector("tbody .v3-empty"));
        }
    }

    [Fact]
    public void Office_and_identified_legacy_receipts_are_disclosed_without_becoming_remittable_cash()
    {
        ServePosition(Position(0m, 0m) with
        {
            Collectors = [],
            LegacyCollectionsOutsideRemittance = 875m,
            OfficeOriginCollected = 1250m,
            OfficeOriginCount = 2
        });
        var cut = RenderComponent<Remittances>();
        cut.WaitForAssertion(() =>
        {
            var note = cut.Find("[aria-label='Receipts outside collector remittance']").TextContent;
            Assert.Contains("Identified legacy collector receipts", note);
            Assert.Contains("₱875.00", note);
            Assert.Contains("Office-origin canonical Collections", note);
            Assert.Contains("₱1,250.00", note);
            Assert.Contains("No SRC-backed collector Collections ready to remit.", cut.Markup);
        }, Timeout);
        _api.Verify(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()), Times.Never);
    }

    [Fact]
    public void Route_IsOfficeOnly_AndTheWorkspaceShowsMoney_NeverPhysicalForms()
    {
        var routes = typeof(Remittances).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Select(x => x.Template).ToList();
        Assert.Contains("/remittances", routes);                              // the primary address
        Assert.Contains("/accountable-forms/remittances", routes);            // the old address still lands here
        Assert.Equal("SuperAdmin,Admin",
            Assert.Single(typeof(Remittances).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        ServePosition(Position());

        var cut = RenderComponent<Remittances>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Remittance & Liquidation", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            var summary = cut.Find("dl[aria-label='Cash position']").TextContent;
            Assert.Contains("₱20,000.00", summary);
            Assert.Contains("Unremitted", summary);
            Assert.DoesNotContain("Selected", summary);
            Assert.DoesNotContain("History status", cut.Markup);
            Assert.DoesNotContain("Remit selected", cut.Markup);
            Assert.Equal("/remittances/new", cut.FindAll("a").Single(a => a.TextContent.Trim() == "New Remittance").GetAttribute("href"));
            Assert.Empty(cut.FindAll("input[type='checkbox']"));
            Assert.Contains("For remittance", cut.Markup);
            Assert.Contains("View all", cut.Markup);
            Assert.Contains("Remittance history", cut.Markup);
            // Only the selected section is shown: For remittance first, the others behind their tabs.
            Assert.Equal("true", cut.Find("button.rm-tab.active").GetAttribute("aria-pressed"));
            Assert.Equal("For remittance", cut.Find("button.rm-tab.active").TextContent.Trim());
            Assert.Empty(cut.FindAll("[aria-label='Collector position']"));
            Assert.Empty(cut.FindAll("[aria-label='Remittance history']"));
            // The position carries a form count (9,275 on hand); the money workspace never shows it, as a count or as pesos.
            Assert.DoesNotContain("9,275", cut.Markup);
            foreach (var retired in new[] { "on hand", "Register received book", "Stock", "custody", "Assign", "Transfer", "Return unused",
                         "cancellation", "lost form", "Needs review", "View accountability", "physical", "Accountable Forms" })
                Assert.DoesNotContain(retired, cut.Markup, StringComparison.OrdinalIgnoreCase);
        }, Timeout);

        OpenTab(cut, "Collector position");
        var row = Assert.Single(cut.FindAll("[aria-label='Collector position'] tbody tr"));
        Assert.Contains("Ana Reyes", row.TextContent);
        Assert.Empty(cut.FindAll("[aria-label='Collections for remittance']"));
        OpenTab(cut, "Remittance history");
        Assert.NotEmpty(cut.FindAll("[aria-label='Remittance history']"));
    }

    [Fact]
    public void TheOldFormsAddress_RedirectsToRemittance_AndRendersNoFormWorkspace()
    {
        Assert.Equal("/accountable-forms",
            Assert.Single(typeof(AccountableFormsRedirect).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        var navigation = Services.GetRequiredService<NavigationManager>();

        var cut = RenderComponent<AccountableFormsRedirect>();

        Assert.EndsWith("/remittances", navigation.Uri);
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void CollectorFilter_ChoosesOneOrMany_SummarisesThem_AndAsksTheServerForTheUnion()
    {
        var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var many = new Mock<ICollectorsApiClient>();
        many.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[]
        {
            new CollectorListDto(CollectorId, "Ana Reyes", "a@example.test", "C-001", [], 0m, 0, null, true),
            new CollectorListDto(b, "Bobby Mercado", "b@example.test", "C-002", [], 0m, 0, null, true),
            new CollectorListDto(c, "Cora Lim", "c@example.test", "C-003", [], 0m, 0, null, true)
        }));
        Services.AddSingleton(many.Object);
        ServePosition(Position());
        RemittanceReviewRequest? filtered = null;
        _api.Setup(x => x.GetFilteredHistoryAsync(It.IsAny<RemittanceReviewRequest>()))
            .Callback<RemittanceReviewRequest>(r => filtered = r)
            .ReturnsAsync(Result<IReadOnlyList<RemittanceHistoryRowDto>>.Success([]));

        var cut = RenderComponent<Remittances>();
        cut.WaitForAssertion(() => Assert.Equal("All collectors", cut.Find("#rm-collectors-value").TextContent.Trim()), Timeout);

        cut.Find(".rm-picker-btn").Click();
        cut.FindAll(".rm-picker-list label").Single(l => l.TextContent.Contains("Bobby")).QuerySelector("input")!.Change(true);
        Assert.Equal("Bobby Mercado", cut.Find("#rm-collectors-value").TextContent.Trim());
        cut.FindAll(".rm-picker-list label").Single(l => l.TextContent.Contains("Cora")).QuerySelector("input")!.Change(true);
        Assert.Equal("Bobby Mercado + 1", cut.Find("#rm-collectors-value").TextContent.Trim());

        cut.Find("form.rm-filters").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(filtered);
            Assert.Equal(new[] { b, c }.OrderBy(x => x), filtered!.CollectorIds!.OrderBy(x => x));
        }, Timeout);

        cut.FindAll(".rm-picker-list label").First().QuerySelector("input")!.Change(true);       // "All collectors" clears the selection
        Assert.Equal("All collectors", cut.Find("#rm-collectors-value").TextContent.Trim());
    }

    [Fact]
    public void EligibleCollectionsAreListedWithTheirSrc_SelectingThemTotals_AndTheRemittanceIsRecordedWithTheSelectionAndOperationId()
    {
        ServePosition(Position(20000m, 19920m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        RecordRemittanceRequest? sent = null;
        _api.Setup(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()))
            .Callback<RecordRemittanceRequest>(r => sent = r)
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(Detail(80m, 70m)));

        var cut = RenderComponent<NewRemittance>();

        // The primary view: unremitted canonical collections, by SRC, with the instrument only as a badge.
        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Collections for remittance'] tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.DoesNotContain("SRC", rows[0].TextContent);   // not a primary column of the selection table
            Assert.Contains("Ana Reyes", rows[0].TextContent);
            Assert.Contains("Market Fees", rows[0].TextContent);
            Assert.Contains("Walk-up", rows[0].TextContent);
            Assert.Contains("Cash Ticket", rows[0].TextContent);            // spoken instrument name on the badge
            Assert.Contains("Unremitted", rows[0].TextContent);
            Assert.Contains("₱30.00", rows[0].TextContent);
        }, Timeout);
        // Nothing is selected until the Head selects it, so nothing can be remitted by accident.
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review Remittance").HasAttribute("disabled"));

        cut.Find("input[aria-label='Select all collections']").Change(true);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("₱80.00", cut.Find("[aria-label='Collections for remittance'] tfoot").TextContent);
            Assert.Contains("₱80.00", cut.Find("dl[aria-label='Cash position']").TextContent);   // Selected
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review Remittance").Click();

        var form = "form[aria-label='Confirm remittance']";
        cut.WaitForAssertion(() =>
        {
            var total = cut.Find("dl[aria-label='Remittance total']").TextContent;
            Assert.Contains("Collectors1", total.Replace(" ", "").Replace("\n", ""));
            Assert.Contains("₱80.00", total);
        }, Timeout);

        // A shortfall stays visible as a difference and is flagged for review; an excess cannot be recorded.
        cut.Find($"{form} input[type='number']").Change("70");
        cut.WaitForAssertion(() =>
        {
            var reconcile = cut.Find("dl.rm-reconcile").TextContent;
            Assert.Contains("₱10.00", reconcile);
            Assert.Contains("Short · for review", reconcile);
        }, Timeout);
        cut.Find($"{form} input[type='number']").Change("90");
        cut.WaitForAssertion(() => Assert.Contains("Over expected", cut.Find("dl.rm-reconcile").TextContent), Timeout);
        Assert.True(cut.Find("button[type='submit'][form='rm-review-form']").HasAttribute("disabled"));
        cut.Find($"{form} input[type='number']").Change("70");
        Assert.True(cut.Find("button[type='submit'][form='rm-review-form']").HasAttribute("disabled"));
        cut.Find($"{form} input[maxlength='500']").Change("Counted short at turnover");
        cut.Find(form).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((CollectorId, 70m), (sent!.CollectorId, sent.AmountRemitted));
            Assert.Equal("Counted short at turnover", sent.Remarks);
            Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
            Assert.Equal(new[] { CollectionA, CollectionB }.Order(), sent.CollectionIds!.Order());
            Assert.Contains("for review", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void OneCheckoutIsOneExpandableRow_SelectingItSelectsEveryCollectionOnce_AndTheTotalsStayPerCollection()
    {
        var other = Guid.NewGuid();
        var scope = Scope();
        scope = scope with
        {
            Collections = [.. scope.Collections, new RemittanceCollectionDto(other, new DateOnly(2026, 9, 5), "SRC-2026-000103",
                RevenueInstrumentType.OfficialReceipt, "Solo", 20m, [new RemittanceBreakdownDto(Guid.NewGuid(), "Stall Rental", 20m)])],
            ExpectedAmount = 100m,
            Sessions = [new EEMOCantilanSDS.Application.Common.Revenue.CollectionSessionDisplayGroup(Guid.NewGuid(), [CollectionA, CollectionB])]
        };
        ServePosition(Position(20000m, 19900m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(scope));
        RecordRemittanceRequest? sent = null;
        _api.Setup(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()))
            .Callback<RecordRemittanceRequest>(r => sent = r)
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(Detail(80m, 80m)));

        var cut = RenderComponent<NewRemittance>();

        const string rows = "[aria-label='Collections for remittance'] tbody tr";
        cut.WaitForAssertion(() =>
        {
            var shown = cut.FindAll(rows);
            Assert.Equal(2, shown.Count);                                          // the checkout, and the lone collection
            Assert.Contains("One checkout · 2 collections", shown[0].TextContent);
            Assert.Contains("₱80.00", shown[0].TextContent);                         // combined amount
            Assert.Equal("false", shown[0].GetAttribute("aria-expanded"));
        }, Timeout);

        cut.FindAll(rows)[0].Click();                                              // expand: both SRCs, each with its own checkbox
        cut.WaitForAssertion(() =>
        {
            var shown = cut.FindAll(rows);
            Assert.Equal(4, shown.Count);
            Assert.Contains("SRC-2026-000101", shown[1].TextContent);
            Assert.Contains("SRC-2026-000102", shown[2].TextContent);
        }, Timeout);

        cut.Find("input[aria-label^='Include all 2 collections']").Change(true);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("₱80.00", cut.Find("[aria-label='Collections for remittance'] tfoot").TextContent);
            Assert.Contains("Selected · 2 of 3", cut.Find("[aria-label='Collections for remittance'] tfoot").TextContent);
        }, Timeout);

        // One child can still be taken out; the checkout is then partly selected, never double counted.
        cut.FindAll(rows)[2].QuerySelector("input[type='checkbox']")!.Change(false);
        cut.WaitForAssertion(() => Assert.Contains("Selected · 1 of 3", cut.Find("[aria-label='Collections for remittance'] tfoot").TextContent), Timeout);
        cut.Find("input[aria-label^='Include all 2 collections']").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Selected · 2 of 3", cut.Find("[aria-label='Collections for remittance'] tfoot").TextContent), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review Remittance").Click();
        cut.WaitForAssertion(() => cut.Find("form[aria-label='Confirm remittance']"), Timeout);
        cut.Find("form[aria-label='Confirm remittance']").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(new[] { CollectionA, CollectionB }.Order(), sent!.CollectionIds!.Order());   // each underlying collection exactly once
            Assert.Equal(80m, sent.AmountRemitted);
        }, Timeout);
    }

    [Fact]
    public void ACheckoutWhoseCollectionsAreNotAllListedStaysIndividual()
    {
        var scope = Scope() with { Sessions = [new EEMOCantilanSDS.Application.Common.Revenue.CollectionSessionDisplayGroup(Guid.NewGuid(), [CollectionA, Guid.NewGuid()])] };
        ServePosition(Position(20000m, 19920m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(scope));

        var cut = RenderComponent<NewRemittance>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[aria-label='Collections for remittance'] tbody tr").Count);
            Assert.DoesNotContain("One checkout", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void HistoryListsRemittances_WithTheirDifference_AndLinksToTheirViewAndPrint()
    {
        ServePosition(Position());
        var ana = Member("Ana Reyes", 110m, 100m);
        ServeHistory(HistoryRow("Needs review", ana));

        var cut = RenderComponent<Remittances>();
        OpenTab(cut, "Remittance history");

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Remittance history'] tbody tr"));
            Assert.Contains("₱10.00", row.TextContent);
            Assert.Contains("Needs review", row.TextContent);
            Assert.Equal($"/remittances/{ana.RemittanceId}", row.QuerySelector("a")!.GetAttribute("href"));
            var actions = row.QuerySelectorAll(".rm-row-action a").Select(a => (a.TextContent.Trim(), a.GetAttribute("href"))).ToList();
            Assert.Equal(("View", $"/remittances/{ana.RemittanceId}"), actions[0]);
            Assert.Equal(("Print", $"/remittances/{ana.RemittanceId}?print=1"), actions[1]);
        }, Timeout);
    }

    [Fact]
    public void ReviewIsAModal_WithFocusBindingEscapeAndUnchangedSelection()
    {
        var focus = JSInterop.SetupModule("./js/account-review.js");
        focus.Mode = JSRuntimeMode.Loose;
        ServePosition(Position(20000m, 19920m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        var cut = RenderComponent<NewRemittance>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("input[aria-label='Select all collections']")), Timeout);
        cut.Find("input[aria-label='Select all collections']").Change(true);
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Review Remittance").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='dialog'][aria-modal='true']")), Timeout);
        Assert.Contains("80.00", cut.Find("[role='dialog']").TextContent);
        cut.WaitForAssertion(() => Assert.Contains(focus.Invocations, x => x.Identifier == "bind"), Timeout);
        cut.Find("[role='dialog']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")), Timeout);
        Assert.Contains(focus.Invocations, x => x.Identifier == "release");
        _api.Verify(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()), Times.Never);
        Assert.True(cut.Find("input[aria-label='Select all collections']").HasAttribute("checked"));
    }

    [Fact]
    public void OneCollectorShowsTheFullNameOnly_TwoShowPlusOne_ThreeShowPlusTwo_AndThePlusOpensTheWholeList()
    {
        ServePosition(Position());
        var one = HistoryRow("Recorded", Member("Bobby Mercado", 313m, 313m));
        var two = HistoryRow("Recorded", Member("Bobby Mercado", 313m, 313m), Member("Cian Consigna", 200m, 200m));
        var three = HistoryRow("Recorded", Member("Bobby Mercado", 313m, 313m), Member("Cian Consigna", 200m, 200m), Member("Dina Dela Cruz", 100m, 100m));
        ServeHistory(one, two, three);

        var cut = RenderComponent<Remittances>();
        OpenTab(cut, "Remittance history");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Remittance history'] tbody tr");
            Assert.Equal(3, rows.Count);
            Assert.Equal("Bobby Mercado", rows[0].QuerySelector(".rm-collector-cell")!.TextContent.Trim());
            Assert.Empty(rows[0].QuerySelectorAll(".rm-more"));
            Assert.Equal("+1", rows[1].QuerySelector(".rm-more")!.TextContent.Trim());
            Assert.StartsWith("Bobby Mercado", rows[1].QuerySelector(".rm-collector-cell")!.TextContent.Trim());
            Assert.DoesNotContain("Cian Consigna", rows[1].TextContent);     // never concatenated into the cell
            Assert.Equal("+2", rows[2].QuerySelector(".rm-more")!.TextContent.Trim());
            Assert.Contains("₱513.00", rows[1].TextContent);                  // the sum of the two independent records
            // A submission opens its own report; a single remittance opens its own.
            Assert.Equal($"/remittances/group/{two.SubmissionId}", rows[1].QuerySelector("a")!.GetAttribute("href"));
        }, Timeout);

        cut.FindAll("[aria-label='Remittance history'] tbody tr")[2].QuerySelector(".rm-more")!.Click();
        cut.WaitForAssertion(() =>
        {
            var members = cut.Find("ul[aria-label='Collectors in this remittance']").TextContent;
            Assert.Contains("Bobby Mercado", members);
            Assert.Contains("Cian Consigna", members);
            Assert.Contains("Dina Dela Cruz", members);
            Assert.Contains("₱100.00", members);
        }, Timeout);
    }

    [Fact]
    public void AGroupWithOneVoidedCollector_SaysPartlyVoided_RatherThanRecorded()
    {
        ServePosition(Position());
        ServeHistory(HistoryRow("Partly voided", Member("Bobby Mercado"), Member("Cian Consigna", status: RemittanceStatus.Voided)));

        var cut = RenderComponent<Remittances>();
        OpenTab(cut, "Remittance history");

        cut.WaitForAssertion(() =>
            Assert.Contains("Partly voided", Assert.Single(cut.FindAll("[aria-label='Remittance history'] tbody tr")).TextContent), Timeout);
    }

    [Fact]
    public void TheSubmissionReport_ListsEveryCollector_WithTheirOwnCollections_AndTheCombinedTotal()
    {
        var bobby = Detail(80m, 80m);
        var slaughter = bobby.Collections[0] with { Lines =
            [new RemittanceBreakdownDto(Guid.NewGuid(), "Slaughterhouse", 10m),
             new RemittanceBreakdownDto(Guid.NewGuid(), "Slaughterhouse", 20m)] };
        bobby = bobby with { Collections = [slaughter, bobby.Collections[1]] };
        var cian = Detail(40m, 35m) with { Row = Detail(40m, 35m).Row with { CollectorName = "Cian Consigna", Id = Guid.NewGuid() } };
        var submission = Guid.NewGuid();
        var report = new RemittanceSubmissionDto(submission, new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            RevenueInstrumentType.CashTicket, "office", 4, 120m, 115m, 5m, "Needs review", [bobby, cian]);
        _api.Setup(x => x.GetSubmissionAsync(submission)).ReturnsAsync(Result<RemittanceSubmissionDto>.Success(report));

        var cut = RenderComponent<RemittanceSubmission>(p => p.Add(x => x.SubmissionId, submission));

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("[aria-label='Collector summary']");
            var rows = summary.QuerySelectorAll("tbody tr");
            Assert.Equal(2, rows.Length);
            Assert.Contains("Ana Reyes", rows[0].TextContent);
            Assert.Contains("Cian Consigna", rows[1].TextContent);
            Assert.Contains("₱35.00", rows[1].TextContent);
            Assert.Contains("₱5.00", rows[1].TextContent);
            Assert.Contains("₱115.00", summary.QuerySelector("tfoot")!.TextContent);
            Assert.Equal(2, cut.FindAll("section[aria-label^='Collections covered for']").Count);
            var firstSource = cut.Find("[aria-label='Collections of Ana Reyes'] tbody tr td:nth-child(2)").TextContent.Trim();
            Assert.Equal("Slaughterhouse", firstSource); // Two posted charge lines; one source label, never repeated.
            Assert.Contains("SRC-2026-000101", cut.Find("[aria-label='Collections of Ana Reyes']").TextContent);
            Assert.DoesNotContain(submission.ToString(), cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void FailedLoad_SaysSo_InsteadOfShowingAZeroPosition()
    {
        _api.Setup(x => x.GetPositionAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<AccountabilityPositionDto>.Failure("offline"));

        var cut = RenderComponent<Remittances>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("dl[aria-label='Cash position']"));
        }, Timeout);
    }


    [Fact]
    public void SeveralCollectors_EachGetTheirOwnAmountAndReference_AndAreRecordedAsOneBatch()
    {
        var benId = Guid.NewGuid();
        var position = Position(20000m, 19880m);
        ServePosition(position with
        {
            Collectors = [position.Collectors[0] with { Unremitted = 80m },
                new CollectorPositionDto(benId, "Ben Cruz", 40m, 0m, 40m, 0, 0m, [])]
        });
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        var benCollection = Guid.NewGuid();
        _api.Setup(x => x.GetScopeAsync(benId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(new RemittanceScopeDto(benId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null,
                [new RemittanceCollectionDto(benCollection, new DateOnly(2026, 9, 5), "SRC-2026-000201", RevenueInstrumentType.OfficialReceipt, null, 40m,
                    [new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 40m)])],
                40m, [new RemittanceBreakdownDto(Guid.NewGuid(), "Market Fees", 40m)], "SRC-2026-000201", "SRC-2026-000201")));
        RecordRemittanceBatchRequest? sent = null;
        _api.Setup(x => x.RecordBatchAsync(It.IsAny<RecordRemittanceBatchRequest>()))
            .Callback<RecordRemittanceBatchRequest>(r => sent = r)
            .ReturnsAsync(Result<IReadOnlyList<RemittanceDetailDto>>.Success([Detail(80m, 80m), Detail(40m, 35m)]));

        var cut = RenderComponent<NewRemittance>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[aria-label='Collections for remittance'] tbody tr").Count), Timeout);
        cut.Find("input[aria-label='Select all collections']").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review Remittance").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("section.rm-draft").Count), Timeout);
        Assert.Contains("Ana Reyes", cut.FindAll("section.rm-draft")[0].TextContent);
        Assert.Contains("Ben Cruz", cut.FindAll("section.rm-draft")[1].TextContent);
        cut.FindAll("section.rm-draft")[1].QuerySelector("input[type='number']")!.Change("35");
        cut.FindAll("section.rm-draft")[1].QuerySelectorAll(".rm-form-row input")[1].Change("ACK-B");
        cut.FindAll("section.rm-draft")[1].QuerySelector("input[maxlength='500']")!.Change("Counted short at turnover");
        var total = cut.Find("dl[aria-label='Remittance total']").TextContent;
        Assert.Contains("₱120.00", total);                                   // total selected
        Assert.Contains("₱115.00", total);                                   // being remitted

        cut.Find("form[aria-label='Confirm remittance']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(2, sent!.Remittances.Count);
            var ana = sent.Remittances.Single(x => x.CollectorId == CollectorId);
            var ben = sent.Remittances.Single(x => x.CollectorId == benId);
            Assert.Equal((80m, (string?)null), (ana.AmountRemitted, ana.Reference));
            Assert.Equal((35m, (string?)"ACK-B"), (ben.AmountRemitted, ben.Reference));
            Assert.Equal("Counted short at turnover", ben.Remarks);
            Assert.Equal(new[] { benCollection }, ben.CollectionIds);
            Assert.NotEqual(ana.ClientOperationId, ben.ClientOperationId);
            Assert.Contains("2 remittances recorded, one per collector", cut.Markup);
        }, Timeout);
        _api.Verify(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()), Times.Never);
    }

    private static RemittanceDetailDto Detail(decimal expected, decimal remitted) => new(
        new RemittanceRowDto(Guid.NewGuid(), new DateOnly(2026, 9, 30), CollectorId, "Ana Reyes", RevenueInstrumentType.CashTicket, 2,
            expected, remitted, expected - remitted, "ACK-1", RemittanceStatus.Recorded, DateTime.UtcNow),
        new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, "office", null, null, null,
        Scope().Collections, Scope().Breakdown, "SRC-2026-000101", "SRC-2026-000102", expected != remitted);

    [Fact]
    public void PreparationFiltersSelectionToVisibleSources_AndRetryKeepsTheSameOperationIdentity()
    {
        Assert.Equal("/remittances/new", Assert.Single(typeof(NewRemittance).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(typeof(NewRemittance).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        ServePosition(Position(20000m, 19920m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        var requests = new List<RecordRemittanceRequest>();
        var calls = 0;
        _api.Setup(x => x.RecordAsync(It.IsAny<RecordRemittanceRequest>()))
            .Callback<RecordRemittanceRequest>(requests.Add)
            .ReturnsAsync(() => ++calls == 1 ? Result<RemittanceDetailDto>.Failure("Retry this request") : Result<RemittanceDetailDto>.Success(Detail(30m, 30m)));
        var cut = RenderComponent<NewRemittance>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[aria-label='Collections for remittance'] tbody tr").Count), Timeout);
        Assert.DoesNotContain("History status", cut.Markup);
        var source = cut.FindAll("select").Single(s => s.ParentElement!.TextContent.StartsWith("Source"));
        source.Change("Market Fees");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[aria-label='Collections for remittance'] tbody tr")), Timeout);
        cut.Find("input[aria-label='Select all collections']").Change(true);
        Assert.Contains("₱30.00", cut.Find("[aria-label='Selection summary']").TextContent);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Review Remittance").Click();
        cut.Find("form[aria-label='Confirm remittance']").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Retry this request", cut.Markup), Timeout);
        cut.Find("form[aria-label='Confirm remittance']").Submit();
        cut.WaitForAssertion(() => Assert.Equal(2, requests.Count), Timeout);
        Assert.Equal(requests[0].ClientOperationId, requests[1].ClientOperationId);
        Assert.Equal(new[] { CollectionA }, requests[1].CollectionIds);
        Assert.Equal(30m, requests[1].AmountRemitted);
        _api.Verify(x => x.RecordBatchAsync(It.IsAny<RecordRemittanceBatchRequest>()), Times.Never);
    }

    [Fact]
    public void Detail_CollapsesRepeatedChargeClassifications_WithoutChangingMoneyOrSrc()
    {
        var detail = Detail(80m, 80m);
        var original = detail.Collections[0];
        var collection = original with
        {
            Lines =
            [
                new RemittanceBreakdownDto(Guid.NewGuid(), "Slaughterhouse", 10m),
                new RemittanceBreakdownDto(Guid.NewGuid(), "Slaughterhouse", 20m)
            ]
        };
        detail = detail with { Collections = [collection, detail.Collections[1]] };
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id))
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));

        var cut = RenderComponent<RemittanceDetail>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            var covered = cut.FindAll("[aria-label='Collections covered'] tbody tr");
            Assert.Equal(2, covered.Count);
            Assert.Equal("Slaughterhouse", covered[0].QuerySelectorAll("td")[1].TextContent.Trim());
            Assert.Contains("SRC-2026-000101", covered[0].TextContent);
            Assert.Contains("SRC-2026-000102", covered[1].TextContent);
            Assert.Contains("₱80.00", cut.Find("[aria-label='Collections covered'] tfoot").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Detail_ShowsTheDifferenceAndCoverage_AndVoidingIsASeparateReview()
    {
        var detail = Detail(80m, 70m);
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id)).ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));

        var cut = RenderComponent<RemittanceDetail>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("₱10.00", cut.Find("dl[aria-label='Remittance']").TextContent);
            Assert.DoesNotContain("SRC-2026-000101 to SRC-2026-000102", cut.Markup);
            Assert.Contains("2 collections", cut.Find("[aria-label='Remittance details']").TextContent);
            Assert.Contains("Collected", cut.Find("dl[aria-label='Remittance']").TextContent);
            Assert.Contains("₱30.00", cut.Find("[aria-label='Collection breakdown']").TextContent);
            Assert.Equal(2, cut.FindAll("[aria-label='Collections covered'] tbody tr").Count);
            // The void is its own review page: no giant inline form here.
            Assert.Empty(cut.FindAll("form[aria-label='Void remittance']"));
            Assert.Equal($"/remittances/{detail.Row.Id}/void", cut.FindAll("a").Single(a => a.TextContent.Trim() == "Void remittance").GetAttribute("href"));
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Print summary").Click();
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "print");
    }

    [Fact]
    public void AVoidedRemittance_ShowsVoided_AndOffersNoVoidAction()
    {
        var detail = Detail(80m, 80m);
        detail = detail with { Row = detail.Row with { Status = RemittanceStatus.Voided }, VoidReason = "Counted short", VoidedBy = "head" };
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id)).ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));

        var cut = RenderComponent<RemittanceDetail>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Voided", cut.Find("header.rd-header").TextContent);
            Assert.Contains("Counted short", cut.Find("[aria-label='Remittance details']").TextContent);
            Assert.DoesNotContain(cut.FindAll("a"), a => a.TextContent.Trim() == "Void remittance");
        }, Timeout);
    }

    [Fact]
    public void TheVoidReview_NeedsAReason_StatesTheConsequence_AndVoidsOnlyThatRemittance()
    {
        var detail = Detail(80m, 70m);
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id)).ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));
        VoidRemittanceRequest? sent = null;
        _api.Setup(x => x.VoidAsync(detail.Row.Id, It.IsAny<VoidRemittanceRequest>()))
            .Callback<Guid, VoidRemittanceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(detail with { Row = detail.Row with { Status = RemittanceStatus.Voided } }));

        var cut = RenderComponent<RemittanceVoidReview>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Void Remittance", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Contains("This will return the covered collections to Unremitted.", cut.Markup);
            Assert.True(cut.Find("form[aria-label='Void remittance'] button[type='submit']").HasAttribute("disabled"));
        }, Timeout);
        cut.Find("form[aria-label='Void remittance'] input").Input("Counted short");
        cut.Find("form[aria-label='Void remittance']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Counted short", sent!.Reason);
            Assert.EndsWith($"/remittances/{detail.Row.Id}", Services.GetRequiredService<NavigationManager>().Uri);
        }, Timeout);
    }

    [Fact]
    public void AnAlreadyVoidedRemittance_CannotBeReviewedForVoidingAgain()
    {
        var detail = Detail(80m, 80m);
        detail = detail with { Row = detail.Row with { Status = RemittanceStatus.Voided } };
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id)).ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));

        var cut = RenderComponent<RemittanceVoidReview>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("already voided", cut.Markup);
            Assert.Empty(cut.FindAll("form[aria-label='Void remittance']"));
        }, Timeout);
        _api.Verify(x => x.VoidAsync(It.IsAny<Guid>(), It.IsAny<VoidRemittanceRequest>()), Times.Never);
    }

    [Fact]
    public void TheVoidList_OffersReviewForRecordedRemittances_AndViewForVoidedOnes_AndTheOverviewHasNoHistoryStatusFilter()
    {
        var recorded = Detail(80m, 80m).Row;
        var voided = recorded with { Id = Guid.NewGuid(), Status = RemittanceStatus.Voided };
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(),
                It.IsAny<RevenueInstrumentType?>(), RemittanceStatus.Recorded))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Success(new[] { recorded }));
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(),
                It.IsAny<RevenueInstrumentType?>(), RemittanceStatus.Voided))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Success(new[] { voided }));

        var list = RenderComponent<RemittanceVoid>();
        list.WaitForAssertion(() =>
        {
            Assert.Equal($"/remittances/{recorded.Id}/void", Assert.Single(list.FindAll("tbody a")).GetAttribute("href"));
            Assert.DoesNotContain("SRC-", list.Markup);                                   // SRC is not a primary column here
        }, Timeout);
        list.FindAll("button").Single(b => b.TextContent.Trim() == "Voided").Click();
        list.WaitForAssertion(() =>
            Assert.Equal($"/remittances/{voided.Id}", Assert.Single(list.FindAll("tbody a")).GetAttribute("href")), Timeout);

        ServePosition(Position());
        var overview = RenderComponent<Remittances>();
        overview.WaitForAssertion(() =>
        {
            Assert.Equal("/remittances/void", overview.FindAll("a").Single(a => a.TextContent.Trim() == "Void Remittance").GetAttribute("href"));
            Assert.DoesNotContain("History status", overview.Markup);
        }, Timeout);
    }

    // ── Collection detail from the For remittance rows ──
    private void ServeActivity(params CollectionActivityEventDto[] events)
    {
        var activity = new Mock<ICollectionActivityApiClient>();
        activity.Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<FacilityCode?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new(new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 3), "Business date", "Net",
                DateTime.UtcNow, events, events.Length, false, 0m, events.Sum(e => e.Amount), 0m, events.Sum(e => e.NetAmount), [])));
        Services.AddSingleton(activity.Object);
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
    }

    private static CollectionActivityEventDto FishMeatEvent() => new("k", "Canonical", "Fish/Meat", CollectionA, new DateOnly(2026, 9, 3), DateTime.UtcNow,
        "SRC-2026-000101", null, RevenueInstrumentType.CashTicket, [], [], "Aling Rosa", null, CollectorId, "Ana Reyes", "Ana Reyes", null,
        "Fish/Meat vendor", null, 65m, 0m, 65m, "Posted", null,
        [new CollectionActivityLineDto("FISH_MEAT", "Fish/Meat", 50m, "GovernedService", null, null, null, null, "Fish vending fee", "Fish/Meat"),
         new CollectionActivityLineDto("FISH_MEAT", "Fish/Meat", 15m, "GovernedService", null, null, null, null, "Ice fee", "Fish/Meat")], []);

    [Fact]
    public void ARowOpensItsRecordedDetail_WithEnterOrClick_ClosesWithEscape_AndTheCheckboxNeverOpensIt()
    {
        ServePosition(Position(20000m, 19920m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        ServeActivity(FishMeatEvent());

        var cut = RenderComponent<NewRemittance>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[aria-label='Collections for remittance'] tbody tr").Count), Timeout);

        // Ticking a row selects it and opens nothing.
        cut.FindAll("[aria-label='Collections for remittance'] tbody tr")[0].QuerySelector("input[type='checkbox']")!.Change(true);
        Assert.Empty(cut.FindAll("[role='dialog']"));
        Assert.Contains("₱30.00", cut.Find("[aria-label='Selection summary']").TextContent);

        var row = cut.FindAll("[aria-label='Collections for remittance'] tbody tr")[0];
        Assert.Equal("0", row.GetAttribute("tabindex"));
        row.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[role='dialog']");
            Assert.Contains("SRC-2026-000101", dialog.TextContent);
            Assert.Contains("Aling Rosa", dialog.TextContent);
            Assert.Contains("Fish vending fee", dialog.TextContent);               // the recorded charge lines, not a reconstruction
            Assert.Contains("Ice fee", dialog.TextContent);
            Assert.Contains("₱65.00", dialog.TextContent);
            Assert.Contains("Not yet remitted", dialog.TextContent);
            Assert.Contains("not an official receipt", dialog.TextContent);
        }, Timeout);
        // Opening the detail did not change the selection.
        Assert.Contains("₱30.00", cut.Find("[aria-label='Selection summary']").TextContent);

        cut.Find("[role='dialog']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role='dialog']"));

        cut.FindAll("[aria-label='Collections for remittance'] tbody tr")[1].Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role='dialog']")), Timeout);
    }

    [Fact]
    public void WhenTheRecordedDetailCannotBeRead_OnlyTheClassificationTotalsAreShown_NeverInventedCharges()
    {
        ServePosition(Position(20000m, 19920m));
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope()));
        ServeActivity();

        var cut = RenderComponent<NewRemittance>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[aria-label='Collections for remittance'] tbody tr").Count), Timeout);
        cut.FindAll("[aria-label='Collections for remittance'] tbody tr")[0].Click();

        cut.WaitForAssertion(() =>
        {
            var dialog = cut.Find("[role='dialog']");
            Assert.Contains("Market Fees", dialog.TextContent);
            Assert.Contains("₱30.00", dialog.TextContent);
            Assert.Contains("could not be read", dialog.TextContent);
        }, Timeout);
    }

    [Fact]
    public void RepeatedParentNamesReadOnce_WhileEveryLineStillCountsInTheTotal()
    {
        ServePosition(Position(20000m, 19920m));
        var slaughter = new RemittanceCollectionDto(CollectionA, new DateOnly(2026, 9, 3), "SRC-2026-000101", RevenueInstrumentType.CashTicket, "Pedro", 90m,
            [new(Guid.NewGuid(), "Slaughterhouse", 30m), new(Guid.NewGuid(), "Slaughterhouse", 40m), new(Guid.NewGuid(), "Slaughterhouse", 20m)]);
        _api.Setup(x => x.GetScopeAsync(CollectorId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<RevenueInstrumentType?>()))
            .ReturnsAsync(Result<RemittanceScopeDto>.Success(Scope() with { Collections = [slaughter], ExpectedAmount = 90m }));

        var cut = RenderComponent<NewRemittance>();
        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[aria-label='Collections for remittance'] tbody tr"));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(row.TextContent, "Slaughterhouse").Count);
            Assert.Contains("₱90.00", row.TextContent);
        }, Timeout);
    }
}
