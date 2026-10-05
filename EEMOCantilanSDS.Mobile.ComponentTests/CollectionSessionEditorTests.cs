using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class CollectionSessionEditorTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 10, 5);
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly List<PendingOperation> _queue = [];
    private readonly Guid _payer = Guid.NewGuid(), _stall = Guid.NewGuid();
    private readonly Connectivity _connection = new();
    private sealed class Connectivity : IConnectivityMonitor { public bool Online = true; public bool IsOnline => Online; public event Action? ConnectivityRestored { add { } remove { } } }
    public CollectionSessionEditorTests()
    {
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => _queue.ToArray());
        store.Setup(x => x.AddIssuedDocumentOperationAsync(It.IsAny<PendingOperation>())).Callback<PendingOperation>(x => _queue.Add(x)).Returns(Task.CompletedTask);
        store.Setup(x => x.UpdateAsync(It.IsAny<PendingOperation>())).Returns(Task.CompletedTask);
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("tenant:collector");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, _connection, owner.Object));
        _api.Setup(x => x.SearchCollectionSessionPayorsAsync("Lisa")).ReturnsAsync(Result<IReadOnlyList<CollectionPayorDto>>.Success([new(_payer, "Lisa Ilogans")]));
        _api.Setup(x => x.GetCollectionSessionDiscoveryAsync(It.IsAny<Guid?>())).ReturnsAsync((Guid? id) => Result<CollectionSessionDiscovery>.Success(new(id, Today,
            [new(CollectionSessionItemKind.GovernedService, "LANDING_BERTHING", "Landing / Berthing", true, true, null, null, false, []),
             new(CollectionSessionItemKind.Electricity, "ECF", "Electricity", true, id.HasValue, null, null, true, []),
             new(null, "NPM_WHOLE_PAYMENT", "NPM Whole payment", false, false, "NotSupportedYet", null, true, [])],
            ElectricitySources: id.HasValue ? [new(Guid.Empty, Guid.Empty, CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity, 0, _stall, "12", "Market", "Vegetable Area", 2026, 10,
                0, 0, 0, 0, 0, 0, 0, SettlementAuthority.Legacy, id, "Lisa Ilogans", Guid.NewGuid(), Guid.NewGuid(), "Electricity", RevenueInstrumentType.OfficialReceipt, "DirectCollection", true, true)] : [],
            ServiceTerms: [new(null, new("LANDING_BERTHING", "Landing / Berthing", false, GovernedServiceBasis.FixedAmount, 200m, null, RevenueInstrumentType.CashTicket, true))])));
        _api.Setup(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>())).ReturnsAsync((CollectionSessionIntent i) =>
        {
            var items = i.Items.Select(x => new CollectionSessionItemQuote(x.ClientItemId, x.Kind, x.Kind == CollectionSessionItemKind.Electricity ? "ECF" : "LANDING_BERTHING",
                x.Kind == CollectionSessionItemKind.Electricity ? "Electricity" : "Landing / Berthing", "October 2026", x.Kind == CollectionSessionItemKind.Electricity ? RevenueInstrumentType.OfficialReceipt : RevenueInstrumentType.CashTicket,
                x.ConfirmedAmount, "version", Guid.NewGuid())).ToArray();
            return Result<CollectionSessionQuote>.Success(new(i.ClientCollectionSessionId, i.PayorId, Today, items,
                items.GroupBy(x => x.Instrument).Select(x => new CollectionSessionInstrumentTotal(x.Key, x.Sum(y => y.Amount))).ToArray(), items.Sum(x => x.Amount), "reviewed", []));
        });
        _api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>())).ReturnsAsync((SyncOfflineCollectionsCommand c) =>
        {
            var operation = c.Operations.Single(); var intent = operation.CollectionSession!.Intent;
            var result = new CollectionSessionResult(intent.ClientCollectionSessionId, CollectionSessionStatus.Recorded, intent.PayorId, intent.Items.Sum(x => x.ConfirmedAmount),
                intent.Items.Select((x, n) => new CollectionSessionCollection(Guid.NewGuid(), $"SRC-2026-{n + 1:000000}", x.Kind == CollectionSessionItemKind.Electricity ? RevenueInstrumentType.OfficialReceipt : RevenueInstrumentType.CashTicket, x.ConfirmedAmount, [x.ClientItemId], "Posted")).ToArray(), []);
            return Result<SyncOfflineCollectionsResultDto>.Success(new(1, 0, 0, [new(operation.ClientOperationId, SyncResultStatus.Synced, null, CollectionSession: result)]));
        });
    }
    private static void Click(IRenderedComponent<CollectionSessionEditor> view, string label) => view.FindAll("button").First(x => x.TextContent.Trim() == label).Click();
    private IRenderedComponent<CollectionSessionEditor> Open() => RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today));
    private static void AddLanding(IRenderedComponent<CollectionSessionEditor> view)
    {
        Click(view, "+ Add item"); Click(view, "Landing / Berthing ›"); Click(view, "Add item");
    }
    [Fact]
    public void Empty_and_eligible_picker_preserve_optional_payer_and_do_not_offer_unsupported_sources()
    {
        var view = Open(); Assert.Contains("No items added.", view.Markup);
        Click(view, "+ Add item"); Assert.DoesNotContain("NPM Whole payment", view.Markup);
        Click(view, "Landing / Berthing ›"); Assert.Contains("₱200.00", view.Markup); Click(view, "Add item");
        Click(view, "Review collection"); Assert.Contains("Cash Ticket", view.Markup);
        Click(view, "Remove"); Assert.Contains("No items added.", view.Markup);
        Assert.DoesNotContain("OR number", view.Markup); Assert.DoesNotContain("LANDING_BERTHING", view.Markup);
    }
    [Fact]
    public void Payer_direct_electricity_and_mixed_instruments_render_every_server_src()
    {
        var view = Open(); view.Find("input").Change("Lisa"); Click(view, "Search"); Click(view, "Lisa Ilogans ›");
        AddLanding(view);
        Click(view, "+ Add item"); Click(view, "Electricity ›");
        Click(view, "Select"); view.Find("button[role=option]").Click();
        view.Find("input[type=number]").Change("44"); Click(view, "Add item"); Click(view, "Review collection");
        Assert.Contains("Official Receipt", view.Markup); Assert.Contains("₱244.00", view.Markup);
        Click(view, "Record collection");
        view.WaitForAssertion(() => { Assert.Contains("SRC-2026-000001", view.Markup); Assert.Contains("SRC-2026-000002", view.Markup); });
        Assert.Single(_queue); Assert.Equal(2, _queue[0].CollectionSession!.Intent.Items.Count);
    }
    [Fact]
    public void Reviewed_offline_intent_waits_without_claiming_a_reference()
    {
        var view = Open(); AddLanding(view); Click(view, "Review collection"); _connection.Online = false;
        Click(view, "Record collection"); view.WaitForAssertion(() => Assert.Contains("Waiting to sync", view.Markup));
        Assert.DoesNotContain("SRC-2026", view.Markup); Assert.Single(_queue);
    }
    [Fact]
    public void Quote_failure_identifies_review_and_disables_recording()
    {
        _api.Setup(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>())).ReturnsAsync((CollectionSessionIntent i) =>
            Result<CollectionSessionQuote>.Success(new(i.ClientCollectionSessionId, null, Today, [], [], 0, null, [new(i.Items[0].ClientItemId, "AmountChanged", "Amount changed.")])));
        var view = Open(); AddLanding(view); Click(view, "Review collection");
        Assert.Contains("Needs review", view.Markup); Assert.Contains("Amount changed.", view.Markup);
        Assert.True(view.FindAll("button").Single(x => x.TextContent.Trim() == "Record collection").HasAttribute("disabled"));
    }
}
