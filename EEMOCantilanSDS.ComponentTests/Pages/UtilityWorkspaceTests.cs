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
            Assert.Contains("ECF", title);
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
    public void Ecf_EligibleObligation_OffersAddToCollection_AndTheSummaryUsesServerTotals()
    {
        Services.AddSingleton(EcfApi(EcfQuote(outstanding: 80m, canAdd: true, SettlementAuthority.Canonical)).Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"), r => r.TextContent.Contains("Juan Dela Cruz"));
            Assert.Contains("Add to collection", Assert.Single(row.QuerySelectorAll("button")).TextContent);
            Assert.Contains("₱80.00", cut.Find("dl[aria-label='ECF position']").TextContent);
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
            Assert.Contains("WCF", title);
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
            Assert.Contains(cut.FindAll("h2"), h => h.TextContent.Trim() == "Cash Ticket exceptions");
            Assert.Contains("1 of 1 open for collection", cut.Markup);
            Assert.Contains("1 unassigned CT in office", cut.Markup);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/accountable-forms");
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
            Assert.Contains("No outstanding WCF obligations", cut.Markup);
            foreach (var meter in new[] { "meter", "cubic", "m³", "consumption ×" })
                Assert.DoesNotContain(meter, cut.Find("table").TextContent, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
    }

    [Fact]
    public void Wcf_WithoutAvailableTickets_SaysSoInTheReadinessLine()
    {
        Services.AddSingleton(WcfApi([WcfQuote(outstanding: 10m, canCollect: true)], []).Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
            Assert.Contains("No unassigned CTs in office", cut.Markup), Timeout);
    }

    [Fact]
    public void Wcf_WhenNoSourceIsCanonical_ReadinessSaysMobileCannotIssueYet()
    {
        // In-office stock is not "available for field collection": a collector issues only tickets assigned to them,
        // and only against a source the server has opened for canonical settlement.
        Services.AddSingleton(WcfApi([WcfQuote(outstanding: 10m, canCollect: false)], [Ticket()]).Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Not yet active for Mobile", cut.Markup);
            Assert.DoesNotContain("available for field collection", cut.Markup);
        }, Timeout);
    }

    private static Mock<IEcfCollectionsApiClient> EcfApi(params EcfObligationQuoteDto[] quotes)
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(quotes));
        api.Setup(x => x.GetAvailableReceiptsAsync())
            .ReturnsAsync(Result<IReadOnlyList<EcfAvailableDocumentDto>>.Success(Array.Empty<EcfAvailableDocumentDto>()));
        api.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(Result<EcfCollectionDraftDto>.NotFound());
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success(Array.Empty<EcfCollectionActivityDto>()));
        return api;
    }

    private static Mock<IWcfCollectionsApiClient> WcfApi(
        IReadOnlyList<WcfObligationQuoteDto>? quotes = null, IReadOnlyList<CashTicketDocumentDto>? tickets = null)
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetObligationsAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfObligationQuoteDto>>.Success(quotes ?? []));
        api.Setup(x => x.GetAvailableCashTicketsAsync())
            .ReturnsAsync(Result<IReadOnlyList<CashTicketDocumentDto>>.Success(tickets ?? []));
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
