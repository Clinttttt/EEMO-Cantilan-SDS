using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// ECF and WCF share the V3 utility workspace presentation. These tests pin what the redesign must keep true:
/// identity and instrument, the page never nesting a second main landmark, and collection actions shown only under
/// the conditions the page already used — never an outstanding balance presented as settled.
/// </summary>
public sealed class UtilityWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public UtilityWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Ecf_ShowsItsIdentityAndOfficialReceipt_InsideTheLayoutMain()
    {
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            var title = Assert.Single(cut.FindAll("h1")).TextContent;
            Assert.Contains("Electricity Consumption Fees", title);
            Assert.Contains("ECF", cut.Find("header").TextContent);
            Assert.Contains("Official Receipt", cut.Find("header").TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/ecf/accounts");
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/ecf/report");
        }, Timeout);
    }

    [Fact]
    public void Ecf_OutstandingObligationThatCannotBeAdded_IsNeverLabelledSettled()
    {
        Services.AddSingleton(EcfApi(EcfQuote(outstanding: 120m, canAdd: false, SettlementAuthority.Legacy)).Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"), r => r.TextContent.Contains("Juan Dela Cruz"));
            Assert.Contains("Not open for collection", row.TextContent);
            Assert.DoesNotContain("Settled", row.TextContent);
        }, Timeout);
    }

    [Fact]
    public void Ecf_ControlsShareOneGroupUnderThePayerAccountsHeading_AndAnEmptyCurrentCollectionIsACompactSummary()
    {
        Services.AddSingleton(EcfApi(EcfQuote(outstanding: 80m, canAdd: true, SettlementAuthority.Canonical)).Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find(".ecf-panel-header");
            Assert.Equal("Payer accounts", header.QuerySelector("h2")!.TextContent.Trim());
            Assert.Empty(header.QuerySelectorAll(".ecf-panel-title .v3-panel-meta"));
            Assert.DoesNotContain("0 of 0", header.TextContent);

            var controls = cut.Find(".ecf-panel-header .ecf-controls");                                   // search, area and the ECF pages together
            Assert.NotNull(controls.QuerySelector("input[type='search']"));
            Assert.NotNull(controls.QuerySelector("select[aria-label='Filter by area']"));
            Assert.Equal(new[] { "Refresh", "Accounts", "Statements", "Report" }, controls.QuerySelectorAll("[aria-label='ECF actions'] .v3-btn").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Empty(cut.FindAll(".ecf-toolbar"));

            var current = cut.Find("[aria-label='Current collection']");
            Assert.Contains("Items", current.TextContent);
            Assert.Contains("₱0.00", current.TextContent);
            Assert.DoesNotContain("Start from a Payor", cut.Markup);                                      // a summary of state, not an advertisement
            Assert.DoesNotContain("No collection in progress", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void Ecf_EligibleObligation_OffersAddToCollection_AndTheSummaryUsesServerTotals()
    {
        Services.AddSingleton(EcfApi(EcfQuote(outstanding: 80m, canAdd: true, SettlementAuthority.Canonical)).Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"), r => r.TextContent.Contains("Juan Dela Cruz"));
            Assert.Contains("Add to collection", Assert.Single(row.QuerySelectorAll("button")).TextContent);
            Assert.Contains("₱80.00", cut.Find("dl.v3h-figures").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Wcf_ShowsItsIdentityAndCashTicket_InsideTheLayoutMain()
    {
        Services.AddSingleton(WcfApi().Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            var title = Assert.Single(cut.FindAll("h1")).TextContent;
            Assert.Contains("Water Consumption Fees", title);
            Assert.Contains("WCF", cut.Find("header").TextContent);
            Assert.Contains("Cash Ticket", cut.Find("header").TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/water-consumption-fees/report");
        }, Timeout);
    }

    [Fact]
    public void Wcf_IsReadOnly_WithNoWebCollectionEntryOrCustodyForms()
    {
        var api = WcfApi([WcfQuote(outstanding: 10m, canCollect: true)], [Ticket()]);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"), r => r.TextContent.Contains("Maria Santos"));
            Assert.Contains("Open for field collection", row.TextContent);
            Assert.Empty(row.QuerySelectorAll("button"));

            Assert.Empty(cut.FindAll("form"));
            Assert.Empty(cut.FindAll("[role='dialog']"));
            Assert.DoesNotContain("Receive book", cut.Markup);
            Assert.DoesNotContain("Assign range", cut.Markup);
            Assert.DoesNotContain("Post collection", cut.Markup);

            // Read-only evidence and reporting stay.
            Assert.Contains(cut.FindAll("h2"), h => h.TextContent.Trim() == "Collection activity");
            Assert.DoesNotContain(cut.FindAll("h2"), h => h.TextContent.Trim() == "Collections needing review");     // an empty review list is not shown
            Assert.DoesNotContain("unassigned CT", cut.Markup);   // physical stock never gates readiness (IA-062)
        }, Timeout);

        api.Verify(x => x.PostAsync(It.IsAny<WcfCollectionPostRequest>()), Times.Never);
        api.Verify(x => x.GetBooksAsync(), Times.Never);
    }

    [Fact]
    public void Wcf_PresentsTheDirectApprovedCashTicketOperation_WithNoMeterWording()
    {
        Services.AddSingleton(WcfApi([], [Ticket()]).Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cash Ticket", cut.Markup);
            var header = cut.Find(".wcf-panel-header");
            Assert.Equal("Payer accounts", header.QuerySelector("h2")!.TextContent.Trim());
            Assert.Empty(header.QuerySelectorAll(".wcf-panel-title .v3-panel-meta"));
            Assert.Contains("No outstanding WCF obligations", cut.Markup);
            foreach (var meter in new[] { "meter", "cubic", "m³", "consumption ×" })
                Assert.DoesNotContain(meter, cut.Find("table").TextContent, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
    }

    [Fact]
    public void Wcf_ReadinessNeverMentionsPhysicalCashTicketStock()
    {
        Services.AddSingleton(WcfApi([WcfQuote(outstanding: 10m, canCollect: true)], []).Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("unassigned CT", cut.Markup), Timeout);
    }

    [Fact]
    public void Wcf_WhenNoSourceIsCanonical_KeepsTheRowStateWithoutAHeaderSubtitle()
    {
        // In-office stock is not "available for field collection": a collector issues only tickets assigned to them,
        // and only against a source the server has opened for canonical settlement.
        Services.AddSingleton(WcfApi([WcfQuote(outstanding: 10m, canCollect: false)], [Ticket()]).Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var header = cut.Find(".wcf-panel-header");
            Assert.Equal("Payer accounts", header.QuerySelector("h2")!.TextContent.Trim());
            Assert.Empty(header.QuerySelectorAll(".wcf-panel-title .v3-panel-meta"));
            Assert.DoesNotContain("Not yet active for Mobile", header.TextContent);
            Assert.Contains("Not open for collection", cut.Find("section[aria-labelledby='wcf-obligations-title'] tbody tr").TextContent);
            Assert.DoesNotContain("available for field collection", cut.Markup);
        }, Timeout);
    }

    private static Mock<IEcfCollectionsApiClient> EcfApi(params EcfObligationQuoteDto[] quotes)
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(quotes));
        api.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(Result<EcfCollectionDraftDto>.NotFound());
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success(Array.Empty<EcfCollectionActivityDto>()));
        return api;
    }

    // ── WCF Mobile collection: one operation-level enable, readiness derived by the server ──

    private static WcfMobileStatusDto Status(bool active = false, params WcfReadinessItemDto[] blockers) => new(
        active, active ? new DateOnly(2026, 10, 1) : null, active ? DateTime.UtcNow : null, active ? "head" : null,
        !active && blockers.Length == 0, blockers,
        [new WcfReadinessItemDto("POLICY", "Water policy", "Cash Ticket · direct amount", true, null, null)],
        [new WcfCollectorReadinessDto(Guid.NewGuid(), "Bobby Mercado", true, blockers.Length == 0 ? 4999 : 0, 0, blockers.Length == 0)],
        0);

    [Fact]
    public void WcfMobile_ReadyToEnable_OffersOneAction_AndAsksTheOfficeToTypeNothing()
    {
        var api = WcfApi([], [Ticket()]);
        api.Setup(x => x.GetMobileStatusAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status()));
        api.Setup(x => x.EnableMobileAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status(active: true)));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() => Assert.Contains("Ready to enable", cut.Find("section.wcf-mobile").TextContent), Timeout);
        var panel = cut.Find("section.wcf-mobile");
        // No attestation checklist, evidence reference, app-version input or separate readiness check.
        Assert.Empty(panel.QuerySelectorAll("input"));
        Assert.DoesNotContain("Check readiness", cut.Markup);
        Assert.DoesNotContain("Evidence reference", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Enable Mobile Collection").Click();
        cut.WaitForAssertion(() => Assert.Contains("Historical Water records", cut.Find("[role='dialog']").TextContent), Timeout);
        Assert.Empty(cut.Find("[role='dialog']").QuerySelectorAll("input"));
        cut.Find("[role='dialog'] .v3-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("section.wcf-mobile"));                                                 // active: nothing to say about it
            Assert.Empty(cut.FindAll("[role='dialog']"));
        }, Timeout);
        api.Verify(x => x.EnableMobileAsync(), Times.Once);
    }

    [Fact]
    public void WcfMobile_Blocked_ShowsOnlyTheRealBlocker_WithItsAction()
    {
        var api = WcfApi([], []);
        api.Setup(x => x.GetMobileStatusAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status(false,
            new WcfReadinessItemDto("CASH_TICKETS", "Cash Ticket custody", "Cash Tickets have not been assigned to Bobby Mercado.",
                false, "Manage Accountable Forms", "/accountable-forms"))));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Needs attention", cut.Find("section.wcf-mobile").TextContent);
            var blocker = Assert.Single(cut.FindAll(".wcf-blockers li"));
            Assert.Contains("Bobby Mercado", blocker.TextContent);
            Assert.Equal("/accountable-forms", blocker.QuerySelector("a")!.GetAttribute("href"));
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Enable Mobile Collection");
        }, Timeout);
    }

    [Fact]
    public void WcfMobile_Active_StatesItPlainly_WithNoEnableAction()
    {
        var api = WcfApi([], [Ticket()]);
        api.Setup(x => x.GetMobileStatusAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status(active: true)));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("section.wcf-mobile"));                                                 // active: nothing to say about it
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Enable Mobile Collection");
            foreach (var technical in new[] { "Canonical", "Settlement authority", "Pending cutover", "cutover" })
                Assert.DoesNotContain(technical, cut.Markup, StringComparison.OrdinalIgnoreCase);
            // The readiness panel and its prose are gone from the working view; the capability itself is unchanged.
            foreach (var verbose in new[] { "View readiness details", "Direct amount ·", "Collectors record Water Consumption Fees on Mobile", "Optional prepared amounts" })
                Assert.DoesNotContain(verbose, cut.Markup);
            Assert.NotNull(cut.Find("dl.v3h-figures"));                                                      // the same hero family as ECF
            Assert.Contains(cut.FindAll(".wcf-controls a"), a => a.GetAttribute("href") == "/operations/water-consumption-fees/accounts");
        }, Timeout);
    }

    [Fact]
    public void WcfActivity_ShowsReadableFacts_NeverTheStoredJsonEvidence()
    {
        var api = WcfApi([], [Ticket()]);
        const string evidence = "{\"schemaVersion\":1,\"municipalityId\":\"6b8e1a52-0000-0000-0000-000000000001\",\"utilityBillId\":\"6b8e1a52-0000-0000-0000-000000000002\",\"stallNo\":\"F-12\",\"billingYear\":2026,\"billingMonth\":9,\"ratePerCubicMeter\":0}";
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfCollectionActivityDto>>.Success(
            [
                new WcfCollectionActivityDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), DateTime.UtcNow, "CT000101", "Maria Santos",
                    120m, 1, "Posted",
                    [new WcfCollectionActivityLineDto("WCF", 120m, 2026, 9, "UtilityBill / Water", evidence, [evidence])])
            ]));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var facts = cut.Find("dl.wcf-facts").TextContent;
            Assert.Contains("F-12", facts);
            Assert.Contains("Water (WCF)", facts);
            Assert.Contains("CT000101", facts);
            foreach (var technical in new[] { "schemaVersion", "municipalityId", "utilityBillId", "ratePerCubicMeter", "{", "6b8e1a52" })
                Assert.DoesNotContain(technical, cut.Markup);
        }, Timeout);
    }

    private static Mock<IWcfCollectionsApiClient> WcfApi(
        IReadOnlyList<WcfObligationQuoteDto>? quotes = null, IReadOnlyList<CashTicketDocumentDto>? tickets = null)
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Success(quotes ?? []));
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfCollectionActivityDto>>.Success(Array.Empty<WcfCollectionActivityDto>()));
        api.Setup(x => x.GetReconciliationExceptionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<WcfReconciliationExceptionDto>>.Success(Array.Empty<WcfReconciliationExceptionDto>()));
        api.Setup(x => x.GetBooksAsync())
            .ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success(Array.Empty<AccountableFormBookDto>()));
        return api;
    }

    private static Mock<ICollectorsApiClient> CollectorsApi()
    {
        var api = new Mock<ICollectorsApiClient>();
        api.Setup(x => x.GetAllCollectorsAsync())
            .ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(Array.Empty<CollectorListDto>()));
        return api;
    }

    private static EcfObligationQuoteDto EcfQuote(decimal outstanding, bool canAdd, SettlementAuthority authority) => new(
        Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 1,
        Guid.NewGuid(), "12", "Tenant Market", "Dry Goods", 2026, 9, 100m, 140m, 40m, 2m,
        outstanding, 0m, outstanding, authority, null, "Juan Dela Cruz",
        Guid.NewGuid(), Guid.NewGuid(), "ECF", RevenueInstrumentType.OfficialReceipt, "Metered", canAdd, canAdd);

    private static WcfObligationQuoteDto WcfQuote(decimal outstanding, bool canCollect) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "7", "Tenant Market", "Fish",
        2026, 9, 10m, 12m, 2m, 5m, outstanding, 0m, outstanding,
        SettlementAuthority.Canonical, null, "Maria Santos",
        Guid.NewGuid(), Guid.NewGuid(), "WCF", RevenueInstrumentType.CashTicket, "Metered", canCollect);

    private static CashTicketDocumentDto Ticket() =>
        new(Guid.NewGuid(), "CT000101", AccountableDocumentState.Assigned, null, 101);
}
