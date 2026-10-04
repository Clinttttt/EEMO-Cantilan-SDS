using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
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
            var row = Assert.Single(cut.FindAll("[aria-label='Collector position'] tbody tr"));
            Assert.Contains("Ana Reyes", row.TextContent);
            // The position carries a form count (9,275 on hand); the money workspace never shows it, as a count or as pesos.
            Assert.DoesNotContain("9,275", cut.Markup);
            foreach (var retired in new[] { "on hand", "Register received book", "Stock", "custody", "Assign", "Transfer", "Return unused",
                         "cancellation", "lost form", "Needs review", "View accountability", "physical", "Accountable Forms" })
                Assert.DoesNotContain(retired, cut.Markup, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
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
        Assert.True(cut.Find($"{form} button[type='submit']").HasAttribute("disabled"));
        cut.Find($"{form} input[type='number']").Change("70");
        cut.Find(form).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((CollectorId, 70m), (sent!.CollectorId, sent.AmountRemitted));
            Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
            Assert.Equal(new[] { CollectionA, CollectionB }.Order(), sent.CollectionIds!.Order());
            Assert.Contains("for review", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void HistoryListsRemittances_WithTheirDifference_AndLinksToTheirViewAndPrint()
    {
        ServePosition(Position());
        var ana = Member("Ana Reyes", 110m, 100m);
        ServeHistory(HistoryRow("Needs review", ana));

        var cut = RenderComponent<Remittances>();

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
    public void OneCollectorShowsTheFullNameOnly_TwoShowPlusOne_ThreeShowPlusTwo_AndThePlusOpensTheWholeList()
    {
        ServePosition(Position());
        var one = HistoryRow("Recorded", Member("Bobby Mercado", 313m, 313m));
        var two = HistoryRow("Recorded", Member("Bobby Mercado", 313m, 313m), Member("Cian Consigna", 200m, 200m));
        var three = HistoryRow("Recorded", Member("Bobby Mercado", 313m, 313m), Member("Cian Consigna", 200m, 200m), Member("Dina Dela Cruz", 100m, 100m));
        ServeHistory(one, two, three);

        var cut = RenderComponent<Remittances>();

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

        cut.WaitForAssertion(() =>
            Assert.Contains("Partly voided", Assert.Single(cut.FindAll("[aria-label='Remittance history'] tbody tr")).TextContent), Timeout);
    }

    [Fact]
    public void TheSubmissionReport_ListsEveryCollector_WithTheirOwnCollections_AndTheCombinedTotal()
    {
        var bobby = Detail(80m, 80m);
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
        var total = cut.Find("dl[aria-label='Remittance total']").TextContent;
        Assert.Contains("₱120.00", total);                                   // total selected
        Assert.Contains("₱115.00", total);                                   // being remitted

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Confirm Remittance").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(2, sent!.Remittances.Count);
            var ana = sent.Remittances.Single(x => x.CollectorId == CollectorId);
            var ben = sent.Remittances.Single(x => x.CollectorId == benId);
            Assert.Equal((80m, (string?)null), (ana.AmountRemitted, ana.Reference));
            Assert.Equal((35m, (string?)"ACK-B"), (ben.AmountRemitted, ben.Reference));
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
    public void Detail_ShowsTheDifferenceAndCoverage_AndAVoidNeedsAReason()
    {
        var detail = Detail(80m, 70m);
        _api.Setup(x => x.GetDetailAsync(detail.Row.Id)).ReturnsAsync(Result<RemittanceDetailDto>.Success(detail));
        VoidRemittanceRequest? sent = null;
        _api.Setup(x => x.VoidAsync(detail.Row.Id, It.IsAny<VoidRemittanceRequest>()))
            .Callback<Guid, VoidRemittanceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<RemittanceDetailDto>.Success(detail with { Row = detail.Row with { Status = RemittanceStatus.Voided }, VoidReason = "Counted short" }));

        var cut = RenderComponent<RemittanceDetail>(p => p.Add(x => x.Id, detail.Row.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("₱10.00", cut.Find("dl[aria-label='Remittance']").TextContent);
            Assert.DoesNotContain("SRC-2026-000101 to SRC-2026-000102", cut.Markup);
            Assert.Contains("2 collections", cut.Find("[aria-label='Remittance details']").TextContent);
            Assert.Contains("Collected", cut.Find("dl[aria-label='Remittance']").TextContent);
            Assert.Contains("₱30.00", cut.Find("[aria-label='Collection breakdown']").TextContent);
            Assert.Equal(2, cut.FindAll("[aria-label='Collections covered'] tbody tr").Count);
            Assert.True(cut.Find("form[aria-label='Void remittance'] button").HasAttribute("disabled"));
        }, Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Print summary").Click();
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "print");

        cut.Find("form[aria-label='Void remittance'] input").Change("Counted short");
        cut.Find("form[aria-label='Void remittance']").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Counted short", sent!.Reason);
            Assert.Empty(cut.FindAll("form[aria-label='Void remittance']"));
            Assert.Contains("Voided", cut.Find("header.rd-header").TextContent);
        }, Timeout);
    }

}
