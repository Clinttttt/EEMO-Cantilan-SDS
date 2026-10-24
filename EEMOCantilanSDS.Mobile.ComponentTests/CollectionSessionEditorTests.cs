using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>
/// New Collection is source-native: search the real source, take only what the server offers for it, or continue as a direct collection.
/// Nothing is matched by name, priced here or inferred; every amount and SRC is the server's.
/// </summary>
public sealed class CollectionSessionEditorTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly List<PendingOperation> _queue = [];
    private readonly Mock<IPendingOperationStore> _store = new();
    private readonly Connectivity _connection = new();
    private static readonly CollectionSourceIdentity FishVendor = new(SourceIdentityKind.FishMeatVendorRegistration, Guid.NewGuid());
    private static readonly CollectionSourceIdentity Occupancy = new(SourceIdentityKind.Occupancy, Guid.NewGuid());
    private static readonly CollectionSourceIdentity Kanmanggay = new(SourceIdentityKind.SpaceAccount, Guid.NewGuid());

    private sealed class Connectivity : IConnectivityMonitor { public bool Online = true; public bool IsOnline => Online; public event Action? ConnectivityRestored { add { } remove { } } }

    public CollectionSessionEditorTests()
    {
        var store = _store;
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => _queue.ToArray());
        store.Setup(x => x.AddIssuedDocumentOperationAsync(It.IsAny<PendingOperation>())).Callback<PendingOperation>(x => _queue.Add(x)).Returns(Task.CompletedTask);
        store.Setup(x => x.UpdateAsync(It.IsAny<PendingOperation>())).Returns(Task.CompletedTask);
        store.Setup(x => x.RemoveAsync(It.IsAny<Guid>())).Callback<Guid>(id =>
            _queue.RemoveAll(x => x.ClientOperationId == id)).Returns(Task.CompletedTask);
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("tenant:collector");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, _connection, owner.Object));
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([
            new(FishVendor, "Pantom Dant", "Meat · 2026", CollectorOperationCodes.FishMeatVendorFee, 2026, FishMeatVendorType.Meat),
            new(Kanmanggay, "Juan Cruz", "Kanmanggay · Space 4", CollectorOperationCodes.KanmanggaySpaceRental),
            new(Occupancy, "Ana Reyes", "New Public Market · Stall 1", "FACILITY_NPM")]));
        // The unified finder is the registered-source browser: it reads the same sources through the bounded browse contract, narrowed by the typed term.
        _api.Setup(x => x.BrowseCollectionSourcesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((string? c, string? term, int skip, int take) =>
        {
            var all = _api.Object.SearchCollectionSourcesAsync(term).GetAwaiter().GetResult().Value ?? [];
            var matched = all.Where(x => string.IsNullOrWhiteSpace(term) || x.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Context.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            return Result<CollectionSourcePage>.Success(new(matched.Skip(skip).Take(take).ToArray(), matched.Count, [new("npm", "NPM"), new("spaces", "Spaces")]));
        });
        _api.Setup(x => x.GetPendingNpmRequestAsync(It.IsAny<Guid>())).ReturnsAsync(Result<NpmRegistrationRequestDto>.NotFound());
        _api.Setup(x => x.GetOfficeActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<SourceNativeActivityDto>>.Success([]));
        _api.Setup(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>())).ReturnsAsync((CollectionSessionIntent i) =>
        {
            var items = i.Items.Select(x => new CollectionSessionItemQuote(x.ClientItemId, x.Kind, x.Native?.OperationCode ?? "OP", Name(x), "Context",
                x.Native?.OperationCode == CollectorOperationCodes.Terminal ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt,
                x.ConfirmedAmount, "version", Guid.NewGuid())).ToArray();
            return Result<CollectionSessionQuote>.Success(new(i.ClientCollectionSessionId, null, Today, items,
                items.GroupBy(x => x.Instrument).Select(x => new CollectionSessionInstrumentTotal(x.Key, x.Sum(y => y.Amount))).ToArray(), items.Sum(x => x.Amount), "reviewed", []));
        });
        _api.Setup(x => x.SyncOfflineCollectionsAsync(It.IsAny<SyncOfflineCollectionsCommand>())).ReturnsAsync((SyncOfflineCollectionsCommand c) =>
        {
            var operation = c.Operations.Single(); var intent = operation.CollectionSession!.Intent;
            var result = new CollectionSessionResult(intent.ClientCollectionSessionId, CollectionSessionStatus.Recorded, intent.PayorId, intent.Items.Sum(x => x.ConfirmedAmount),
                intent.Items.Select((x, n) => new CollectionSessionCollection(Guid.NewGuid(), $"SRC-2026-{n + 1:000000}",
                    x.Native?.OperationCode == CollectorOperationCodes.Terminal ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt, x.ConfirmedAmount, [x.ClientItemId], "Posted")).ToArray(), []);
            return Result<SyncOfflineCollectionsResultDto>.Success(new(1, 0, 0, [new(operation.ClientOperationId, SyncResultStatus.Synced, null, CollectionSession: result)]));
        });
    }

    [Fact]
    public void Rejected_unposted_checkout_requires_explicit_server_verified_discard()
    {
        var checkout = Guid.NewGuid();
        var fee = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 56m,
            Native: new SourceNativeChargeIntent(CollectorOperationCodes.FishMeatVendorFee));
        var parking = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Water, 30m);
        _queue.Add(new PendingOperation {
            ClientOperationId = checkout, OwnerKey = "tenant:collector",
            Kind = OfflineOperationKind.ItemizedCollectionSession,
            LocalStatus = PendingLocalStatus.ReconciliationRequired,
            CollectionSession = new(new(checkout, Today, null, [fee, parking]), "prior-quote"),
            CollectionSessionResult = new(checkout, CollectionSessionStatus.NeedsReview, null, 86m,
                [], [new(fee.ClientItemId, "RateChanged", "The original amount needs review.")]),
            ReceivedAmount = 86m
        });
        _api.Setup(x => x.GetCollectionSessionAsync(checkout))
            .ReturnsAsync(Result<CollectionSessionResult>.NotFound());

        var cut = RenderComponent<CollectionSessionEditor>(p =>
            p.Add(x => x.BusinessDate, Today).Add(x => x.ResumeSessionId, checkout));
        Assert.Contains("Needs review", cut.Markup);
        Assert.Contains("Discard checkout", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent == "Discard checkout").Click();
        Assert.Single(_queue);
        Assert.Contains("Confirm discard", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent == "Confirm discard").Click();
        cut.WaitForAssertion(() => Assert.Empty(_queue));
        _api.Verify(x => x.GetCollectionSessionAsync(checkout), Times.AtLeastOnce);
        _store.Verify(x => x.RemoveAsync(checkout), Times.Once);
    }

    [Fact]
    public void Rejected_checkout_cannot_be_discarded_if_server_has_a_posted_collection()
    {
        var checkout = Guid.NewGuid();
        var fee = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Water, 56m);
        _queue.Add(new PendingOperation {
            ClientOperationId = checkout, OwnerKey = "tenant:collector",
            Kind = OfflineOperationKind.ItemizedCollectionSession,
            LocalStatus = PendingLocalStatus.ReconciliationRequired,
            CollectionSession = new(new(checkout, Today, null, [fee]), "prior-quote"),
            CollectionSessionResult = new(checkout, CollectionSessionStatus.NeedsReview, null, 56m,
                [], [new(fee.ClientItemId, "RateChanged", "Needs review.")])
        });
        _api.Setup(x => x.GetCollectionSessionAsync(checkout)).ReturnsAsync(
            Result<CollectionSessionResult>.Success(new(checkout, CollectionSessionStatus.Recorded,
                null, 56m, [new(Guid.NewGuid(), "SRC-TEST", RevenueInstrumentType.OfficialReceipt,
                    56m, [fee.ClientItemId], "Posted")], [])));
        var cut = RenderComponent<CollectionSessionEditor>(p =>
            p.Add(x => x.BusinessDate, Today).Add(x => x.ResumeSessionId, checkout));
        cut.FindAll("button").Single(x => x.TextContent == "Discard checkout").Click();
        cut.FindAll("button").Single(x => x.TextContent == "Confirm discard").Click();
        cut.WaitForAssertion(() => Assert.Contains("cannot be discarded", cut.Markup));
        Assert.Single(_queue);
        _store.Verify(x => x.RemoveAsync(checkout), Times.Never);
    }

    private static string Name(CollectionSessionItemIntent item) => item.Native?.OperationCode switch
    {
        CollectorOperationCodes.FishMeatVendorFee => "Fish / Meat Vendor Fee",
        CollectorOperationCodes.WeightAndMeasure => "Weight & Measure",
        CollectorOperationCodes.Terminal => "Income From Terminal",
        _ => "Collection item"
    };

    private static CollectionSessionCapability Op(CollectionSessionItemKind kind, string code, string name, CollectionFamily family, params CollectionSessionSourceChoice[] choices) =>
        new(kind, code, name, true, true, null, null, false, [], true, choices, family);

    private static CollectionSessionSourceChoice Native(string code, string name, string context, CollectionSessionChoiceIdentity identity, RevenueInstrumentType instrument,
        CollectionSessionAmountRule rule, decimal? rate = null) =>
        new(code + "|" + name, CollectionSessionItemKind.SourceNative, code, name, context, identity, instrument, rule, Rate: rate,
            RequiredInputs: rule == CollectionSessionAmountRule.QuantityRate ? ["Kilograms"] : ["AmountReceived"]);

    private void ServeFishVendor() => _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(FishVendor)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
        [Op(CollectionSessionItemKind.SourceNative, CollectorOperationCodes.FishMeatVendorFee, "Fish / Meat Vendor Fee", CollectionFamily.Market,
                Native(CollectorOperationCodes.FishMeatVendorFee, "Fish / Meat Vendor Fee", "Pantom Dant", new(VendorRegistrationId: FishVendor.Id), RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.DirectAmount)),
         Op(CollectionSessionItemKind.SourceNative, CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", CollectionFamily.Market,
                Native(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", "Meat", new(VendorRegistrationId: FishVendor.Id), RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.QuantityRate, 66m))],
        SourceIdentity: FishVendor, NpmLinkStatus: LinkedNpmStatus.NotLinked)));

    private void ServeDirect() => _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(null)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
        [Op(CollectionSessionItemKind.SourceNative, CollectorOperationCodes.FishMeatVendorFee, "Fish / Meat Vendor Fee", CollectionFamily.Market,
                Native(CollectorOperationCodes.FishMeatVendorFee, "Fish / Meat Vendor Fee", "Payer Snapshot", new(), RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.DirectAmount)),
         Op(CollectionSessionItemKind.SourceNative, CollectorOperationCodes.Terminal, "Income From Terminal", CollectionFamily.Terminal,
                Native(CollectorOperationCodes.Terminal, "COMFORT ROOM", "Income From Terminal", new(TerminalSection: TerminalSection.ComfortRoom), RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount),
                Native(CollectorOperationCodes.Terminal, "PULL PUL VANS, CARGO VANS", "Income From Terminal", new(TerminalSection: TerminalSection.PullPulVansCargoVans), RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount),
                Native(CollectorOperationCodes.Terminal, "TRICYCAD", "Income From Terminal", new(TerminalSection: TerminalSection.Tricycad), RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount),
                Native(CollectorOperationCodes.Terminal, "Jeepney", "PULL PUL VANS, CARGO VANS", new(TerminalSection: TerminalSection.PullPulVansCargoVans, VehicleClassId: Jeepney), RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount, 20m),
                Native(CollectorOperationCodes.Terminal, "Van", "PULL PUL VANS, CARGO VANS", new(TerminalSection: TerminalSection.PullPulVansCargoVans, VehicleClassId: Guid.NewGuid()), RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount, 20m),
                Native(CollectorOperationCodes.Terminal, "Tricycle", "TRICYCAD", new(TerminalSection: TerminalSection.Tricycad, VehicleClassId: Tricycle), RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount, 5m))])));
    private static readonly Guid Jeepney = Guid.NewGuid(), Tricycle = Guid.NewGuid();

    private IRenderedComponent<CollectionSessionEditor> Open(string? only = null, string[]? also = null) =>
        RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today).Add(x => x.OnlyOperation, only).Add(x => x.AlsoOperations, also));

    // The dedicated Fish / Meat page: the collection family, so a registered vendor is offered what the server confirms (Vendor Fee and Weight & Measure).
    private IRenderedComponent<CollectionSessionEditor> OpenFishMeatPage() => Open(CollectorOperationCodes.FishMeatVendorFee, [CollectorOperationCodes.WeightAndMeasure]);

    [Fact]
    public void VendorRouteKeepsItsTwoItemsAndSeparateSourceTransitions()
    {
        Services.AddSingleton(new MobileSessionService());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/vendor-fee");
        var view = RenderComponent<EEMOCantilanSDS.Mobile.Components.Pages.Menus.NewCollection>();
        var editor = view.FindComponent<CollectionSessionEditor>();
        Assert.Equal(CollectorOperationCodes.FishMeatVendorFee, editor.Instance.OnlyOperation);
        Assert.Equal(new[] { CollectorOperationCodes.WeightAndMeasure }, editor.Instance.AlsoOperations);
        Assert.Equal(4, view.FindAll(".collection-source-actions button").Count);
        Assert.Contains("My registration requests", view.Find(".collection-source-actions").TextContent);
        Assert.Contains("My NPM requests", view.Find(".collection-source-actions").TextContent);
        VegetableFruitModeTests.SaveVisual("mobile-fish-visit", view.Markup);
    }

    [Theory]
    [InlineData("Choose electricity / ECF account", "/ecf")]
    [InlineData("Choose NPM stall", "/market")]
    public void VendorVisitTransitionsRequireExplicitSelectionOfAnotherSource(string label, string route)
    {
        var view = OpenFishMeatPage();
        Assert.Contains("Record them separately", view.Find("[aria-label='Other collection sources']").TextContent);
        Click(view, label);
        Assert.EndsWith(route, Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        Assert.Empty(_queue);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(It.IsAny<CollectionSourceIdentity?>()), Times.Never);
    }

    [Theory]
    [InlineData("TCC")]
    [InlineData("NCC")]
    public void Monthly_stall_without_canonical_rent_uses_existing_operation_and_never_direct_cash(string facility)
    {
        var identity = new CollectionSourceIdentity(SourceIdentityKind.Occupancy, Guid.NewGuid());
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>()))
            .ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([
                new(identity, "Alarcon Ryan", facility + " · Stall 4", "FACILITY_" + facility)
            ]));
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(identity))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today, [], SourceIdentity: identity)));
        var view = Open("FACILITY_" + facility);
        view.Find("input[type=search]").Focus();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-option")));
        ClickSource(view, "Alarcon Ryan");
        view.WaitForAssertion(() => Assert.Contains("Open " + facility + " collections", view.Markup));
        Assert.Contains("Monthly stall rental", view.Markup);
        Assert.DoesNotContain("Continue as direct collection", view.Markup);
        Assert.Empty(view.FindAll(".collection-available-fees button"));
    }

    [Fact]
    public void VendorVisitCannotDiscardUnrecordedItemsBySwitchingToElectricityOrRent()
    {
        ServeFishVendor();
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Focus();
        ClickSource(view, "Pantom Dant");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(x => x.TextContent.Contains("Fish / Meat Vendor Fee")).Click();
        view.Find("[role=dialog] input[type=number]").Input("50");
        Click(view, "Add item");
        // Once a vendor is chosen there is no other source to hop to: the visit stays in this one checkout, so nothing can be discarded by switching.
        Assert.Empty(view.FindAll(".collection-source-actions"));
        Assert.Single(view.FindAll(".item-row"));
        Assert.Empty(_queue);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(Occupancy), Times.Never);
    }

    [Fact]
    public void A_vendor_fee_already_in_the_collection_is_hidden_from_the_list_while_the_other_charges_stay()
    {
        ServeFishVendor();
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Focus();
        ClickSource(view, "Pantom Dant");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(x => x.TextContent.Contains("Fish / Meat Vendor Fee")).Click();
        view.Find("[role=dialog] input[type=number]").Input("50");
        Click(view, "Add item");

        Click(view, "+ Add item");
        var sheet = view.Find("[role=dialog]").TextContent;
        Assert.DoesNotContain("Fish / Meat Vendor Fee", sheet);                    // charged once: it is not offered again
        Assert.Contains("Weight & Measure", sheet);
    }

    [Fact]
    public void Close_steps_back_to_the_list_and_then_to_the_collection()
    {
        ServeFishVendor();
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Focus();
        ClickSource(view, "Pantom Dant");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(x => x.TextContent.Contains("Fish / Meat Vendor Fee")).Click();

        Click(view, "Close");
        Assert.Contains("Weight & Measure", view.Find("[role=dialog]").TextContent);    // back on the list, not out of the dialog

        Click(view, "Close");
        Assert.Empty(view.FindAll("[role=dialog]"));                                  // then back on the collection
    }
    [Fact]
    public void Edit_reopens_an_added_item_with_what_was_entered()
    {
        ServeFishVendor();
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Focus();
        ClickSource(view, "Pantom Dant");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(x => x.TextContent.Contains("Fish / Meat Vendor Fee")).Click();
        view.Find("[role=dialog] input[type=number]").Input("50");
        Click(view, "Add item");

        view.FindAll(".item-actions button").Single(b => b.TextContent.Trim() == "Edit").Click();     // its own choice is not "already taken"

        Assert.Equal("50", view.Find("[role=dialog] input[type=number]").GetAttribute("value"));
    }

    [Fact]
    public void A_utility_item_reads_as_the_charge_with_a_clean_context_not_as_the_payers_name()
    {
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [Op(CollectionSessionItemKind.Electricity, "ECF", "Electricity", CollectionFamily.Market,
                new CollectionSessionSourceChoice("ECF|stall|v2", CollectionSessionItemKind.Electricity, "ECF", "Ana Reyes", "Ana Reyes ·  · Stall 1",
                    new(StallId: EcfStall, UtilityBillId: EcfBill, OccupancyId: Occupancy.Id, Year: Today.Year, Month: Today.Month),
                    RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.DirectAmount, RequiredInputs: ["AmountReceived"]))],
            SourceIdentity: Occupancy)));
        ServeRecent();
        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(b => b.TextContent.Contains("Electricity")).Click();
        view.Find("[role=dialog] input[type=number]").Input("100");
        Click(view, "Add item");

        var row = Assert.Single(view.FindAll(".item-row"));
        Assert.Equal("Electricity", row.QuerySelector(".item-name")!.TextContent.Trim());
        Assert.Equal("Ana Reyes · Stall 1", row.QuerySelector(".item-context")!.TextContent.Trim());     // no empty separators, no repeats
    }
    [Fact]
    public void Water_already_in_the_collection_never_blocks_adding_the_electricity_of_the_same_stall()
    {
        var water = Guid.NewGuid();
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [Op(CollectionSessionItemKind.Water, "WCF", "Water", CollectionFamily.Market,
                new CollectionSessionSourceChoice("WCF|stall", CollectionSessionItemKind.Water, "WCF", "Water", "New Public Market · Stall 1",
                    new(StallId: EcfStall, UtilityBillId: water, OccupancyId: Occupancy.Id, Year: Today.Year, Month: Today.Month),
                    RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.DirectAmount, RequiredInputs: ["AmountReceived"])),
             Op(CollectionSessionItemKind.Electricity, "ECF", "Electricity", CollectionFamily.Market,
                new CollectionSessionSourceChoice("ECF|stall", CollectionSessionItemKind.Electricity, "ECF", "Electricity", "New Public Market · Stall 1",
                    new(StallId: EcfStall, UtilityBillId: EcfBill, OccupancyId: Occupancy.Id, Year: Today.Year, Month: Today.Month),
                    RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.DirectAmount, RequiredInputs: ["AmountReceived"]))],
            SourceIdentity: Occupancy)));
        ServeRecent();
        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(b => b.TextContent.Contains("Water")).Click();
        view.Find("[role=dialog] input[type=number]").Input("542");
        Click(view, "Add item");

        Click(view, "+ Add item");
        var sheet = view.Find("[role=dialog]");
        Assert.DoesNotContain("Water", sheet.TextContent);                                    // taken
        sheet.QuerySelectorAll(".collection-option").Single(b => b.TextContent.Contains("Electricity")).Click();
        view.Find("[role=dialog] input[type=number]").Input("100");
        Click(view, "Add item");

        Assert.Equal(2, view.FindAll(".item-row").Count);
        Assert.DoesNotContain("already in this collection", view.Markup);
    }

    [Fact]
    public void A_weighing_already_in_the_collection_is_not_offered_again()
    {
        ServeFishVendor();
        _api.Setup(x => x.QuoteOfficeCollectionAsync(It.Is<SourceNativeCollectionRequest>(r => r.Charge.Kilograms == 3m))).ReturnsAsync(Result<SourceNativeChargeQuote>.Success(
            new(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", "Meat", RevenueInstrumentType.OfficialReceipt, 198m, "v1", new(CollectorOperationCodes.WeightAndMeasure), Rate: 66m, VendorType: FishMeatVendorType.Meat)));
        var view = PickSource("Pantom Dant");
        Click(view, "+ Add item"); Click(view, "Weight & Measure ›");
        view.Find("[role=dialog] input[type=number]").Input("3");
        view.WaitForAssertion(() => Assert.False(AddDisabled(view)));
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));

        Click(view, "+ Add item");
        Assert.DoesNotContain("Weight & Measure", view.Find("[role=dialog]").TextContent);     // taken once: never a twin
        Assert.Single(view.FindAll(".item-row"));
    }
    private static readonly Guid DailyStall = Guid.NewGuid();
    [Fact]
    public void Whole_rent_entry_opens_the_shared_amount_screen_with_the_verified_contract()
    {
        var contract = Guid.NewGuid();
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [Op(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", CollectionFamily.Rent,
                new CollectionSessionSourceChoice("Whole|stall", CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", "Stall 2",
                    new(StallId: DailyStall, OccupancyId: contract), RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.MonthlyRemaining))],
            SourceIdentity: Occupancy)));
        var view = PickSource("Ana Reyes"); Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(x => x.TextContent.Contains("Whole payment")).Click();
        Assert.EndsWith($"/npm/whole-payment/{DailyStall}?contractId={contract}", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        Assert.Empty(_queue);
    }

    private void ServeNpmOccupancy() => _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
        [new(CollectionSessionItemKind.NpmDaily, "NPM_DAILY", "Daily stall payment", true, true, null, null, false, [], true,
                [new("Daily|stall", CollectionSessionItemKind.NpmDaily, "NPM_DAILY", "Daily stall payment", "Stall 2 · Oct 7, 2026", new(StallId: DailyStall, OccupancyId: Guid.NewGuid(), PeriodStart: Today),
                    RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.FixedAmount, 30m, RequiredInputs: [])], CollectionFamily.Rent),
         new(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", true, true, null, null, false, [], true,
                [new("Whole|stall", CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", "Stall 2 · October", new(StallId: DailyStall, OccupancyId: Guid.NewGuid(), Year: 2026, Month: 10),
                    RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance, 870m, RequiredInputs: [])], CollectionFamily.Rent)],
        SourceIdentity: Occupancy)));

    [Fact]
    public void Selected_NPM_source_shows_only_its_server_choices_and_never_loads_walk_up_operations()
    {
        ServeDirect(); // Direct choices exist, but selecting the occupancy must never request them.
        CollectionSessionCapability Choice(CollectionSessionItemKind kind, string code, string name, CollectionFamily family,
            CollectionSessionAmountRule rule, decimal? amount) => Op(kind, code, name, family,
                new CollectionSessionSourceChoice(code + "|stall", kind, code, name, "New Public Market · Stall 1",
                    new(StallId: DailyStall, OccupancyId: Occupancy.Id, Year: Today.Year, Month: Today.Month, PeriodStart: Today),
                    RevenueInstrumentType.OfficialReceipt, rule, amount, RequiredInputs: []));
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [Choice(CollectionSessionItemKind.NpmDaily, "NPM_DAILY", "Daily stall payment", CollectionFamily.Rent, CollectionSessionAmountRule.FixedAmount, 47.5m),
             Choice(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", CollectionFamily.Rent, CollectionSessionAmountRule.MonthlyRemaining, 812.5m),
             Choice(CollectionSessionItemKind.Water, "WCF", "Water", CollectionFamily.Market, CollectionSessionAmountRule.DirectAmount, null),
             Choice(CollectionSessionItemKind.Electricity, "ECF", "Electricity", CollectionFamily.Market, CollectionSessionAmountRule.PreparedBalance, 95m)],
            SourceIdentity: Occupancy)));

        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item");
        var picker = view.Find("[role=dialog]");
        Assert.Equal(4, picker.QuerySelectorAll(".collection-option").Length);
        Assert.Contains("Daily stall payment", picker.TextContent);
        Assert.Contains("Whole payment", picker.TextContent);
        Assert.Contains("Water", picker.TextContent);
        Assert.Contains("Electricity", picker.TextContent);
        Assert.Contains("₱47.50", picker.TextContent);
        Assert.Contains("₱812.50", picker.TextContent);
        foreach (var unrelated in new[] { "Market Fees", "Landing / Berthing", "Transfer Large Cattle", "Transportation / Parking", "Income From Terminal" })
            Assert.DoesNotContain(unrelated, picker.TextContent);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(null), Times.Never);
    }


    private static void Click(IRenderedComponent<CollectionSessionEditor> view, string label)
    {
        for (var attempt = 0; ; attempt++)
        {
            var button = view.FindAll("button").FirstOrDefault(x => x.TextContent.Trim() == label)
                ?? throw new InvalidOperationException($"No button \"{label}\" in: " + string.Join(" | ", view.FindAll("button").Select(b => b.TextContent.Trim())));
            try { button.Click(); return; }
            catch (Bunit.Rendering.UnknownEventHandlerIdException) when (attempt < 5) { Thread.Sleep(50); }
        }
    }

    private static void ClickSource(IRenderedComponent<CollectionSessionEditor> view, string name)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = view.FindAll(".payer-option").FirstOrDefault(x => x.QuerySelector("strong")!.TextContent.Trim() == name)
                ?? throw new InvalidOperationException($"No source \"{name}\" in: " + view.Markup);
            try { row.Click(); return; }
            catch (Bunit.Rendering.UnknownEventHandlerIdException) when (attempt < 5) { Thread.Sleep(50); }
        }
    }

    private IRenderedComponent<CollectionSessionEditor> PickSource(string name, string? only = null)
    {
        var view = Open(only);
        if (view.FindAll(".sb").Count == 0) view.Find("input[type=search]").Focus();   // the unified browser lists its sources without a focus step
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-option")));
        ClickSource(view, name);
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));
        return view;
    }

    private static bool AddDisabled(IRenderedComponent<CollectionSessionEditor> view) =>
        view.Find("[role=dialog]").QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled");

    [Fact]
    public void Assessed_fine_defaults_to_remaining_but_accepts_only_valid_partial_amounts()
    {
        var fineId = Guid.NewGuid();
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
                [Op(CollectionSessionItemKind.FineAssessment, RevenueClassificationCodes.PenaltiesAndFines,
                    "Fines / Penalties", CollectionFamily.Rent,
                    new CollectionSessionSourceChoice("FINE|" + fineId.ToString("N"), CollectionSessionItemKind.FineAssessment,
                        RevenueClassificationCodes.PenaltiesAndFines, "Penalty", "Stall 1",
                        new(FineAssessmentId: fineId, FineVersion: 3),
                        RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance,
                        ServerAmount: 320m, MaximumAmount: 320m, RequiredInputs: []))],
                SourceIdentity: Occupancy)));
        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item");
        view.Find("[role=dialog] .collection-option").Click(); // choose Fines / Penalties
        var amount = view.Find("[role=dialog] input[type=number]");
        Assert.Equal("320", amount.GetAttribute("value"));
        Assert.Contains("Outstanding assessed fine", view.Find("[role=dialog]").TextContent);
        foreach (var invalid in new[] { "0", "-1", "320.01", "5.001" })
        {
            amount.Input(invalid);
            Assert.True(AddDisabled(view), $"Amount {invalid} should be rejected.");
        }
        amount.Input("125");
        Assert.False(AddDisabled(view));
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));
        Assert.Contains("125.00", view.Find(".item-row").TextContent);
        Click(view, "Review collection");
        _api.Verify(x => x.QuoteCollectionSessionAsync(It.Is<CollectionSessionIntent>(intent =>
            intent.Items.Count == 1 && intent.Items[0].ConfirmedAmount == 125m &&
            intent.Items[0].Fine != null && intent.Items[0].Fine.AssessmentId == fineId &&
            intent.Items[0].Fine.Version == 3)), Times.Once);
        Assert.Empty(_queue); // quote/review must never create a receipt
    }

    [Fact]
    public void Whole_payment_summary_uses_server_monthly_balance_without_days_covered()
    {
        var view = RenderComponent<NpmWholeSummary>(p => p.Add(x => x.Quote,
            new NpmWholePaymentQuoteDto(Guid.NewGuid(), 2026, 2, Today, 28, 600m, 0m, "reviewed",
                RevenueInstrumentType.OfficialReceipt, MonthlyObligation: 900m, Collected: 300m)));
        Assert.Contains("February 2026", view.Markup);
        Assert.Contains("₱600.00", view.Markup);
        Assert.DoesNotContain("Days covered", view.Markup);
        Assert.Contains("Remaining due", view.Markup); Assert.Contains("Collected", view.Markup);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(view.Markup, "February 2026").Count);              // the period is stated once
    }

    [Fact]
    public void The_landing_lists_sources_without_a_focus_step_each_row_showing_its_name_and_typed_context_and_never_an_id()
    {
        var view = Open();




        view.WaitForAssertion(() =>
        {
            var rows = view.FindAll(".payer-option").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Pantom Dant") && r.Contains("Meat · 2026"));
            Assert.Contains(rows, r => r.Contains("Juan Cruz") && r.Contains("Kanmanggay · Space 4"));
            Assert.Contains(rows, r => r.Contains("Ana Reyes") && r.Contains("New Public Market · Stall 1"));
            Assert.DoesNotContain("FishMeatVendorRegistration", view.Markup);
            Assert.DoesNotContain(FishVendor.Id.ToString(), view.Markup);
        });
        Assert.NotNull(view.Find(".sb-list"));                                          // bounded list; one search field, no second finder
        Assert.Single(view.FindAll("input[type=search]"));
    }

    [Fact]
    public void A_registered_fish_vendor_is_offered_only_the_vendor_fee_and_weight_and_measure()
    {
        ServeFishVendor();
        var view = PickSource("Pantom Dant");

        Click(view, "+ Add item");

        var offered = view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Select(x => x.TextContent.Replace("›", "").Trim()).ToList();
        Assert.Equal(["Fish / Meat Vendor Fee", "Weight & Measure"], offered);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(FishVendor), Times.AtLeastOnce);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(null), Times.Never);       // direct operations were never mixed into the source
    }

    [Theory]
    [InlineData(LinkedNpmStatus.NotAuthorized, "not assigned")]
    [InlineData(LinkedNpmStatus.NotEligibleForDate, "eligible for this collection date")]
    [InlineData(LinkedNpmStatus.AwaitingVerification, "awaiting office verification")]
    [InlineData(LinkedNpmStatus.Withdrawn, "link was withdrawn")]
    public void Npm_link_diagnostics_do_not_offer_duplicate_registration_requests(LinkedNpmStatus status, string text)
    {
        ServeFishVendor();
        var served = _api.Object.GetSourceCollectionDiscoveryAsync(FishVendor).GetAwaiter().GetResult().Value!;
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(FishVendor)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(served with { NpmLinkStatus = status }));
        var view = PickSource("Pantom Dant");
        view.WaitForAssertion(() => Assert.Contains(text, view.Markup));
        Assert.Empty(view.FindComponents<NpmRegistrationRequestForm>());
        Click(view, "+ Add item");
        Assert.Contains(text, view.Find("[role=dialog]").TextContent);
        Assert.Empty(view.FindComponents<NpmRegistrationRequestForm>());
    }

    [Fact]
    public void The_vendor_fee_asks_only_for_the_amount_received_with_no_rate_and_returns_the_server_src()
    {
        ServeFishVendor();
        var view = PickSource("Pantom Dant");
        Click(view, "+ Add item"); Click(view, "Fish / Meat Vendor Fee ›");
        var sheet = view.Find("[role=dialog]");

        Assert.DoesNotContain("rate", sheet.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OR · Official Receipt", sheet.TextContent);
        Assert.True(AddDisabled(view));
        sheet.QuerySelector("input[type=number]")!.Input("120");
        Assert.False(AddDisabled(view));
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Contains("SRC-2026-000001", view.Markup));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((CollectorOperationCodes.FishMeatVendorFee, FishVendor.Id, 120m), (item.Native!.OperationCode, item.Native.VendorRegistrationId, item.ConfirmedAmount));
        Assert.Equal(FishVendor, _queue[0].CollectionSession!.Intent.SourceIdentity);
    }

    [Fact]
    public void Weight_and_measure_takes_kilograms_and_shows_the_servers_calculated_amount_so_Add_item_enables_as_it_is_typed()
    {
        ServeFishVendor();
        _api.Setup(x => x.QuoteOfficeCollectionAsync(It.Is<SourceNativeCollectionRequest>(r => r.Charge.Kilograms == 3m))).ReturnsAsync(Result<SourceNativeChargeQuote>.Success(
            new(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", "Meat", RevenueInstrumentType.OfficialReceipt, 198m, "v1", new(CollectorOperationCodes.WeightAndMeasure), Rate: 66m, VendorType: FishMeatVendorType.Meat)));
        var view = PickSource("Pantom Dant");
        Click(view, "+ Add item"); Click(view, "Weight & Measure ›");
        Assert.True(AddDisabled(view));                                                  // no kilograms yet

        view.Find("[role=dialog] input[type=number]").Input("3");                        // typed, never blurred

        view.WaitForAssertion(() =>
        {
            Assert.False(AddDisabled(view));
            Assert.Contains("₱198.00", view.Find("[role=dialog]").TextContent);          // the amount is the server's quote, not kilograms times a local rate
            Assert.Contains("₱66.00 / kg", view.Find("[role=dialog]").TextContent);
        });
        Click(view, "Add item");
        Assert.Equal(198m, Assert.Single(view.FindAll(".item-row")).QuerySelector(".item-amount strong")!.TextContent.Trim() == "₱198.00" ? 198m : 0m);
    }

    [Fact]
    public void An_unregistered_name_continues_as_a_direct_collection_that_offers_the_vendor_fee_but_never_weight_and_measure()
    {
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([]));
        ServeDirect();
        var view = Open();
        view.Find("input[type=search]").Input("Walk Up Vendor");
        view.WaitForAssertion(() => Assert.Contains("Continue with payer name", view.Markup));

        view.FindAll(".payer-option").First(x => x.TextContent.Contains("Continue with payer name")).Click();
        view.WaitForAssertion(() => Assert.Contains("Walk Up Vendor", view.Find(".payer-selected").TextContent));
        Click(view, "+ Add item");

        var offered = view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Select(x => x.TextContent.Replace("›", "").Trim()).ToList();
        Assert.Contains("Fish / Meat Vendor Fee", offered);
        Assert.DoesNotContain("Weight & Measure", offered);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(null), Times.AtLeastOnce);
        Click(view, "Fish / Meat Vendor Fee ›");
        view.Find("[role=dialog] input[type=number]").Input("50");
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");
        view.WaitForAssertion(() => Assert.Contains("SRC-2026-000001", view.Markup));
        var intent = _queue[0].CollectionSession!.Intent;
        Assert.Null(intent.SourceIdentity);
        Assert.Equal("Walk Up Vendor", intent.PayerSnapshot);                            // a snapshot on the receipt only: no registration or payor is created
    }

    [Fact]
    public void A_source_with_nothing_due_says_so_and_offers_a_separate_direct_collection_rather_than_mixing_operations()
    {
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Kanmanggay)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today, [], SourceIdentity: Kanmanggay)));
        ServeDirect();
        var view = PickSource("Juan Cruz");

        Assert.Contains("No linked collection is due for this source.", view.Markup);
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(null), Times.Never);

        Click(view, "Continue as direct collection");

        view.WaitForAssertion(() =>
        {
            Assert.Contains("Juan Cruz", view.Find(".payer-selected").TextContent);
            Assert.Contains("No linked source", view.Find(".payer-selected").TextContent);
        });
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(null), Times.AtLeastOnce);
    }

    [Fact]
    public void Terminal_takes_a_section_and_direct_amount_without_collecting_ticket_counts()
    {
        ServeDirect();
        var view = Open("TERMINAL");
        view.WaitForAssertion(() => Assert.Equal("Items", view.Find(".collection-row h2").TextContent));
        Assert.Empty(view.FindAll(".collection-payer"));
        Click(view, "+ Add item");
        var sheet = view.Find("[role=dialog]");
        foreach (var section in new[] { "COMFORT ROOM", "PULL PUL VANS, CARGO VANS", "TRICYCAD" }) Assert.Contains(section, sheet.TextContent);
        view.FindAll("button[role=radio]").First(b => b.TextContent.Contains("TRICYCAD")).Click();
        Assert.True(AddDisabled(view));

        sheet = view.Find("[role=dialog]");
        sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("900");
        Assert.Single(sheet.QuerySelectorAll("input[type=number]"));
        Assert.DoesNotContain("Cash Ticket count", sheet.TextContent);
        Assert.Contains("CT · Cash Ticket", view.Find("[role=dialog]").TextContent);
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Contains("SRC-2026-000001", view.Markup));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((TerminalSection.Tricycad, 900m), (item.Native!.Section, item.ConfirmedAmount));
        Assert.Null(item.Native.CashTicketCount);
        Assert.Null(item.Native.VehicleClassId);                                                    // a section total names no vehicle
    }

    private IRenderedComponent<CollectionSessionEditor> OpenTerminalSheet()
    {
        ServeDirect();
        var view = Open("TERMINAL");
        view.WaitForAssertion(() => Assert.Equal("Items", view.Find(".collection-row h2").TextContent));
        Assert.Empty(view.FindAll(".collection-payer"));
        Click(view, "+ Add item");
        return view;
    }

    private static void PickSection(IRenderedComponent<CollectionSessionEditor> view, string name) =>
        view.FindAll("button[role=radio]").First(b => b.TextContent.Contains(name)).Click();

    [Fact]
    public void Terminal_offers_exactly_the_three_official_sections_and_never_a_vehicle_class_as_a_section()
    {
        var view = OpenTerminalSheet();

        var sections = view.FindAll("button[role=radio]").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["COMFORT ROOM", "PULL PUL VANS, CARGO VANS", "TRICYCAD"], sections);
        Assert.DoesNotContain("Jeepney", view.Find("[role=dialog]").TextContent);                    // vehicle types stay hidden until a section with vehicles is in By vehicle type
        Assert.DoesNotContain("Income From Terminal Income", view.Find(".term-sections").TextContent);
        Assert.Empty(view.FindAll(".term-mode"));                                                    // nothing selected yet, so no mode and no entry fields
        Assert.Empty(view.FindAll("[role=dialog] input[type=number]"));
    }

    [Fact]
    public void Comfort_Room_has_no_vehicle_mode_while_the_vehicle_sections_default_to_Section_total()
    {
        var view = OpenTerminalSheet();

        PickSection(view, "COMFORT ROOM");
        Assert.Empty(view.FindAll(".term-mode"));

        PickSection(view, "PULL PUL VANS");
        var mode = view.Find(".term-mode");
        Assert.Equal(["Section total", "By vehicle type"], mode.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.Equal("true", mode.QuerySelectorAll("button")[0].GetAttribute("aria-pressed"));      // Section total is the default
        Assert.NotEmpty(view.FindAll("[role=dialog] input[type=number]"));                           // so the whole amount can be entered at once
    }

    [Fact]
    public void ByVehicleType_lists_only_the_sections_own_vehicles_with_the_servers_rate_and_the_typed_amount_stays_authoritative()
    {
        var view = OpenTerminalSheet();
        PickSection(view, "PULL PUL VANS");
        view.FindAll(".term-mode button")[1].Click();

        var options = view.FindAll("[role=dialog] button[role=option]").Select(b => b.TextContent).ToList();
        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.Contains("Jeepney") && o.Contains("₱20.00"));
        Assert.DoesNotContain(options, o => o.Contains("Tricycle"));                                  // another section's vehicle
        Assert.True(AddDisabled(view));                                                                // no vehicle chosen yet

        view.FindAll("[role=dialog] button[role=option]").First(b => b.TextContent.Contains("Jeepney")).Click();
        Assert.Contains("Approved rate", view.Find("[role=dialog]").TextContent);
        var sheet = view.Find("[role=dialog]");
        sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("1234.50");
        Assert.Single(sheet.QuerySelectorAll("input[type=number]"));
        Assert.False(AddDisabled(view));                                                               // the button reacts while typing
        Click(view, "Add item");

        var row = view.Find(".item-row");
        Assert.Contains("PULL PUL VANS, CARGO VANS", row.TextContent);
        Assert.Contains("Jeepney · ₱20.00 approved rate", row.TextContent);
        Assert.DoesNotContain("cash tickets", row.TextContent);
        Assert.DoesNotContain("Income From Terminal", row.TextContent);
        Click(view, "Review collection"); Click(view, "Record collection");
        view.WaitForAssertion(() => Assert.Single(_queue));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((TerminalSection.PullPulVansCargoVans, Jeepney, 1234.50m), (item.Native!.Section, item.Native.VehicleClassId, item.ConfirmedAmount));
        Assert.Null(item.Native.CashTicketCount);
    }

    [Fact]
    public void A_draft_item_can_be_edited_in_place_or_removed_and_review_needs_an_item()
    {
        var view = OpenTerminalSheet();
        PickSection(view, "TRICYCAD");
        view.Find("[role=dialog] input[type=number]").Input("3200"); Click(view, "Add item");
        Assert.Contains("Section total", view.Find(".item-row").TextContent);

        Click(view, "Edit");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[role=dialog]")));
        Assert.Equal("3200", view.Find("[role=dialog] input[type=number]").GetAttribute("value"));        // what was entered is back in the sheet
        view.Find("[role=dialog] input[type=number]").Input("3300"); Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));                                                           // replaced, not duplicated
        Assert.Contains("₱3,300.00", view.Find(".item-row").TextContent);

        Click(view, "Remove");
        Assert.Empty(view.FindAll(".item-row"));
        Assert.True(view.FindAll("button").Single(b => b.TextContent.Trim() == "Review collection").HasAttribute("disabled"));   // review needs an item
        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Trim() == "Record collection");                          // nothing to confirm before a review
    }


    [Fact]
    public void One_primary_action_follows_the_state_review_then_record_and_any_change_returns_to_review()
    {
        var view = OpenTerminalSheet();
        PickSection(view, "TRICYCAD");
        view.Find("[role=dialog] input[type=number]").Input("3200"); Click(view, "Add item");

        Assert.Equal(["Review collection"], view.FindAll(".collection-actions button").Select(b => b.TextContent.Trim()).ToArray());

        Click(view, "Review collection");
        view.WaitForAssertion(() => Assert.Equal(["Record collection"], view.FindAll(".collection-actions button").Select(b => b.TextContent.Trim()).ToArray()));
        Assert.Contains("₱3,200.00", view.Find(".review-total").TextContent);

        // Changing an item discards the reviewed quote: nothing can be recorded against it, so the action goes back to Review.
        Click(view, "Edit");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[role=dialog]")));
        view.Find("[role=dialog] input[type=number]").Input("3300"); Click(view, "Add item");
        Assert.Equal(["Review collection"], view.FindAll(".collection-actions button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.Empty(view.FindAll(".review-total"));
        Assert.Empty(_queue);
    }
    [Fact]
    public void Several_items_each_get_their_own_server_src_in_the_result()
    {
        ServeFishVendor();
        _api.Setup(x => x.QuoteOfficeCollectionAsync(It.IsAny<SourceNativeCollectionRequest>())).ReturnsAsync(Result<SourceNativeChargeQuote>.Success(
            new(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", "Meat", RevenueInstrumentType.OfficialReceipt, 66m, "v1", new(CollectorOperationCodes.WeightAndMeasure), Rate: 66m)));
        var view = PickSource("Pantom Dant");
        Click(view, "+ Add item"); Click(view, "Fish / Meat Vendor Fee ›");
        view.Find("[role=dialog] input[type=number]").Input("100"); Click(view, "Add item");
        Click(view, "+ Add item"); Click(view, "Weight & Measure ›");
        view.Find("[role=dialog] input[type=number]").Input("1");
        view.WaitForAssertion(() => Assert.False(AddDisabled(view)));
        Click(view, "Add item"); Click(view, "Review collection");

        Assert.Contains("₱166.00", view.Find(".review-total").TextContent);
        Click(view, "Record collection");

        view.WaitForAssertion(() =>
        {
            Assert.Contains("SRC-2026-000001", view.Markup);
            Assert.Contains("SRC-2026-000002", view.Markup);
            Assert.Contains("2 collection records", view.Markup);
        });
        Assert.DoesNotContain("FISH_MEAT_VENDOR_FEE", view.Markup);
        Assert.DoesNotContain("WEIGHT_AND_MEASURE", view.Markup);
    }

    [Fact]
    public void A_reviewed_offline_collection_waits_without_claiming_a_reference()
    {
        ServeFishVendor();
        var view = PickSource("Pantom Dant");
        Click(view, "+ Add item"); Click(view, "Fish / Meat Vendor Fee ›");
        view.Find("[role=dialog] input[type=number]").Input("120"); Click(view, "Add item"); Click(view, "Review collection");
        _connection.Online = false;

        Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Contains("Waiting to sync", view.Markup));
        Assert.DoesNotContain("SRC-2026", view.Markup);
        Assert.Single(_queue);
    }

    [Fact]
    public void The_source_cannot_be_changed_once_items_are_added()
    {
        ServeFishVendor();
        var view = PickSource("Pantom Dant");
        Assert.False(view.FindAll("button").Single(b => b.TextContent.Trim() == "Change").HasAttribute("disabled"));
        Click(view, "+ Add item"); Click(view, "Fish / Meat Vendor Fee ›");
        view.Find("[role=dialog] input[type=number]").Input("120"); Click(view, "Add item");

        Assert.True(view.FindAll("button").Single(b => b.TextContent.Trim() == "Change").HasAttribute("disabled"));
    }

    [Fact]
    public void Recorded_notice_shows_the_server_reference_prominently_and_never_makes_one_up()
    {
        var recorded = RenderComponent<RecordedNotice>(p => p.Add(x => x.Message, "Collection recorded · SRC-2026-000123"));
        Assert.Equal("SRC-2026-000123", recorded.Find(".recorded-ref").TextContent.Trim());
        var waiting = RenderComponent<RecordedNotice>(p => p.Add(x => x.Message, "Saved on this device. Waiting to sync."));
        Assert.Empty(waiting.FindAll(".recorded-ref"));
        Assert.DoesNotContain("SRC-", waiting.Markup);
    }

    [Fact]
    public void The_dedicated_fish_meat_page_offers_a_registered_vendor_both_the_vendor_fee_and_weight_and_measure()
    {
        ServeFishVendor();
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Focus();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-option")));
        ClickSource(view, "Pantom Dant");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));

        Click(view, "+ Add item");

        var offered = view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Select(x => x.TextContent.Replace("›", "").Trim()).ToList();
        Assert.Equal(["Fish / Meat Vendor Fee", "Weight & Measure"], offered);                         // the page no longer hides what the server confirms
        Assert.DoesNotContain(offered, x => x.Contains("NPM")); // no collectible rent without verified tenancy
        Assert.Contains("Request NPM registration", view.Find("[role=dialog]").TextContent);
        Assert.Contains("No cash before approval", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void The_dedicated_fish_meat_page_weighs_with_the_servers_rate_and_amount_never_its_own()
    {
        ServeFishVendor();
        _api.Setup(x => x.QuoteOfficeCollectionAsync(It.IsAny<SourceNativeCollectionRequest>())).ReturnsAsync((SourceNativeCollectionRequest r) => Result<SourceNativeChargeQuote>.Success(
            new(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", "Meat", RevenueInstrumentType.OfficialReceipt, 66m * (r.Charge!.Kilograms ?? 0m), "v1", new(CollectorOperationCodes.WeightAndMeasure), Rate: 66m)));
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Focus();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-option")));
        ClickSource(view, "Pantom Dant");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));
        Click(view, "+ Add item"); Click(view, "Weight & Measure ›");
        Assert.True(AddDisabled(view));

        view.Find("[role=dialog] input[type=number]").Input("3");

        view.WaitForAssertion(() =>
        {
            Assert.Contains("₱198.00", view.Find("[role=dialog]").TextContent);                        // the server's quote for 3 kg, shown
            Assert.False(AddDisabled(view));
        });
        Click(view, "Add item");
        _api.Verify(x => x.QuoteOfficeCollectionAsync(It.Is<SourceNativeCollectionRequest>(r => r.Charge!.Kilograms == 3m && r.Charge.VendorRegistrationId == FishVendor.Id)), Times.AtLeastOnce);
        Assert.Equal(198m, Assert.Single(view.FindAll(".item-amount strong").Select(x => x.TextContent)) is var shown && shown.Contains("198.00") ? 198m : 0m);
    }

    [Fact]
    public void The_dedicated_fish_meat_page_never_offers_weight_and_measure_to_a_walk_up_payer()
    {
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([]));
        ServeDirect();
        var view = OpenFishMeatPage();
        view.Find("input[type=search]").Input("Walk Up Vendor");
        view.WaitForAssertion(() => Assert.Contains("Continue with payer name", view.Markup));
        view.FindAll(".payer-option").First(x => x.TextContent.Contains("Continue with payer name")).Click();
        view.WaitForAssertion(() => Assert.Contains("Walk Up Vendor", view.Find(".payer-selected").TextContent));

        Click(view, "+ Add item");

        var sheet = view.Find("[role=dialog]").TextContent;
        Assert.Contains("Fish / Meat Vendor Fee", sheet);
        Assert.DoesNotContain("Weight & Measure", sheet);
    }

    [Fact]
    public void An_npm_occupancy_offers_the_daily_stall_payment_and_the_whole_payment_with_the_servers_amounts_and_no_npm_prefix()
    {
        ServeNpmOccupancy();
        var view = PickSource("Ana Reyes");

        Click(view, "+ Add item");

        var rows = view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Select(x => x.TextContent.Replace("›", "").Trim()).ToList();
        Assert.Contains(rows, r => r.StartsWith("Daily stall payment") && r.Contains("₱30.00") && r.Contains("Today"));
        Assert.Contains(rows, r => r.StartsWith("Whole payment") && r.Contains("₱870.00") && r.Contains("remaining"));
        Assert.DoesNotContain("NPM", view.Find("[role=dialog]").TextContent);
        Assert.Contains("Rent Income", view.Find("[role=dialog]").TextContent);                       // grouped where the office reads them
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_vendors_linked_stall_rent_is_listed_with_the_vendors_own_charges_in_one_checkout(bool dedicated)
    {
        ServeFishVendor();
        var linked = new CollectionSourceSearchResult(Occupancy, "Ana Reyes", "New Public Market · Stall 1", "FACILITY_NPM");
        ServeNpmOccupancy();
        var served = _api.Object.GetSourceCollectionDiscoveryAsync(FishVendor).GetAwaiter().GetResult().Value!;
        var stall = _api.Object.GetSourceCollectionDiscoveryAsync(Occupancy).GetAwaiter().GetResult().Value!;
        // The server lists the office-linked stall's own rent under the vendor's source: one list, each item its own Collection.
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(FishVendor)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(
            served with { LinkedSources = [linked], Operations = [.. served.Operations, .. stall.Operations] }));
        var view = dedicated ? OpenFishMeatPage() : Open();
        if (dedicated) view.Find("input[type=search]").Focus();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-option")));
        ClickSource(view, "Pantom Dant");

        Assert.DoesNotContain("Linked NPM stall", view.Markup);                     // no separate hop to the stall
        Click(view, "+ Add item");
        var sheet = view.Find("[role=dialog]");
        Assert.Contains("Fish / Meat Vendor Fee", sheet.TextContent);
        Assert.Contains("Daily stall payment", sheet.TextContent);                  // the stall's rent sits in the same list
        sheet.QuerySelectorAll(".collection-option").Single(x => x.TextContent.Contains("Daily stall payment")).Click();
        Click(view, "Add item");

        Assert.Equal("Pantom Dant", view.Find(".payer-selected strong").TextContent.Trim());     // still the vendor's checkout
        Assert.Single(view.FindAll(".item-row"));
        Assert.Empty(_queue);
    }
    [Fact]
    public void An_npm_part_payment_takes_an_amount_up_to_the_servers_maximum_and_goes_in_as_a_part_payment_item()
    {
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [new(CollectionSessionItemKind.NpmPartPayment, "NPM_PART_PAYMENT", "Part payment", true, true, null, null, false, [], true,
                [new("NpmPart|stall", CollectionSessionItemKind.NpmPartPayment, "NPM_PART_PAYMENT", "Part payment", "Stall 2 · October 2026",
                    new(StallId: DailyStall, OccupancyId: Guid.NewGuid(), Year: 2026, Month: 10), RevenueInstrumentType.OfficialReceipt,
                    CollectionSessionAmountRule.DirectAmount, MaximumAmount: 210m, RequiredInputs: ["AmountReceived"])], CollectionFamily.Rent)],
            SourceIdentity: Occupancy)));
        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item");
        Assert.Contains("Any amount up to ₱210.00", view.Find("[role=dialog]").TextContent);     // the server's own ceiling, never one made up here
        Click(view, "Part paymentAny amount up to ₱210.00 ›");

        var amount = view.Find("[role=dialog] input[type=number]");
        Assert.Equal("210", amount.GetAttribute("max"));
        Assert.Contains("oldest unpaid day first", view.Find("[role=dialog]").TextContent);
        amount.Input("300");                                                                       // more than the server will apply
        Assert.True(view.FindAll("[role=dialog] button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled"));
        amount.Input("20");
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Single(_queue));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((CollectionSessionItemKind.NpmPartPayment, 20m, DailyStall, 2026, 10),
            (item.Kind, item.ConfirmedAmount, item.NpmPart!.StallId, item.NpmPart.Year, item.NpmPart.Month));
        Assert.Null(item.NpmDaily); Assert.Null(item.NpmWhole);
        Assert.Equal(Occupancy, _queue[0].CollectionSession!.Intent.SourceIdentity);
    }

    [Fact]
    public void The_daily_stall_payment_is_the_servers_charge_and_goes_in_as_a_daily_item_for_that_stall()
    {
        ServeNpmOccupancy();
        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item"); Click(view, "Daily stall payment₱30.00 · Today ›");

        Assert.Contains("₱30.00", view.Find("[role=dialog]").TextContent);
        Assert.Empty(view.FindAll("[role=dialog] input[type=number]"));                                 // no amount to type: the charge is the server's
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Single(_queue));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((CollectionSessionItemKind.NpmDaily, 30m, DailyStall), (item.Kind, item.ConfirmedAmount, item.NpmDaily!.StallId));
        Assert.Equal(Occupancy, _queue[0].CollectionSession!.Intent.SourceIdentity);
    }

    [Fact]
    public void By_vehicle_type_offers_an_optional_name_after_the_vehicle_and_it_travels_as_the_payer_snapshot()
    {
        var view = OpenTerminalSheet();
        PickSection(view, "PULL PUL VANS");
        Assert.DoesNotContain("Name optional", view.Find("[role=dialog]").TextContent);

        view.FindAll(".term-mode button")[1].Click();
        Assert.DoesNotContain("Name optional", view.Find("[role=dialog]").TextContent);                                         // not until a vehicle is chosen
        view.FindAll("[role=dialog] button[role=option]").First(b => b.TextContent.Contains("Jeepney")).Click();
        var labels = view.FindAll("[role=dialog] label").Select(l => l.TextContent.Trim()).ToList();
        Assert.True(labels.FindIndex(l => l.StartsWith("Name")) < labels.FindIndex(l => l.StartsWith("Amount received")));      // after the vehicle, before the amount
        view.FindAll("[role=dialog] input[type=text]").Single().Input("Mang Pedro");
        view.FindAll("[role=dialog] input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("480");
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Single(_queue));
        Assert.Equal("Mang Pedro", _queue[0].CollectionSession!.Intent.PayerSnapshot);
    }

    [Theory]
    [InlineData("COMFORT ROOM")]
    [InlineData("PULL PUL VANS")]
    [InlineData("TRICYCAD")]
    public void Terminal_section_total_has_no_name_and_keeps_direct_amount_without_creating_a_source(string section)
    {
        var view = OpenTerminalSheet();
        PickSection(view, section);
        var sheet = view.Find("[role=dialog]");
        Assert.Empty(sheet.QuerySelectorAll("input[type=text]"));
        Assert.DoesNotContain("Name optional", sheet.TextContent);
        sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("73.50");
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");
        view.WaitForAssertion(() => Assert.Single(_queue));
        var intent = _queue[0].CollectionSession!.Intent;
        Assert.Null(intent.PayorId);
        Assert.Null(intent.SourceIdentity);
        Assert.Null(intent.PayerSnapshot);
        Assert.Equal(73.5m, Assert.Single(intent.Items).ConfirmedAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Driver")]
    public void Terminal_vehicle_name_is_optional_and_only_a_snapshot(string name)
    {
        var view = OpenTerminalSheet();
        PickSection(view, "PULL PUL VANS");
        view.FindAll(".term-mode button")[1].Click();
        view.FindAll("[role=dialog] button[role=option]").First(b => b.TextContent.Contains("Jeepney")).Click();
        var field = view.Find("[role=dialog] input[type=text]");
        Assert.False(field.HasAttribute("required"));
        field.Input(name);
        view.FindAll("[role=dialog] input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("73.50");
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");
        view.WaitForAssertion(() => Assert.Single(_queue));
        var intent = _queue[0].CollectionSession!.Intent;
        Assert.Null(intent.PayorId);
        Assert.Null(intent.SourceIdentity);
        Assert.Equal(string.IsNullOrWhiteSpace(name) ? null : name, intent.PayerSnapshot);
        Assert.Equal(73.5m, Assert.Single(intent.Items).ConfirmedAmount);
    }

    [Fact]
    public void Terminal_switching_from_vehicle_to_comfort_room_hides_and_clears_uncommitted_name()
    {
        var view = OpenTerminalSheet();
        PickSection(view, "PULL PUL VANS");
        view.FindAll(".term-mode button")[1].Click();
        view.FindAll("[role=dialog] button[role=option]").First(b => b.TextContent.Contains("Jeepney")).Click();
        Assert.Equal("true", view.Find("[role=dialog] button[aria-selected=true]").GetAttribute("aria-selected"));
        view.Find("[role=dialog] input[type=text]").Input("Driver");
        PickSection(view, "COMFORT ROOM");
        Assert.DoesNotContain("Name optional", view.Find("[role=dialog]").TextContent);
        Assert.Empty(view.FindAll("[role=dialog] input[type=text]"));
        view.FindAll("[role=dialog] input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("73.50");
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");
        view.WaitForAssertion(() => Assert.Single(_queue));
        Assert.Null(_queue[0].CollectionSession!.Intent.PayerSnapshot);
    }

    // ── Correcting a recorded collection: the same form, opened on what was recorded; the server reverses the original and posts one replacement ──
    private static MobileRecentCollection TerminalRecent(decimal amount = 3200m) => RecentCollectionsTests.Recent("SRC-2026-000022", amount, "TRICYCAD", edit: true, remove: true,
        instrument: RevenueInstrumentType.CashTicket,
        native: new(Guid.NewGuid(), "SRC-2026-000022", Today, DateTime.UtcNow, "TERMINAL", TerminalSection.Tricycad, null, null, null, "Cora", amount, amount, "Posted", 80, null, null, null, null),
        source: new(null, CollectionSessionItemKind.SourceNative, "TERMINAL", new(TerminalSection: TerminalSection.Tricycad), new(CollectorOperationCodes.Terminal, null, TerminalSection.Tricycad, null, 80)));

    private IRenderedComponent<CollectionSessionEditor> OpenCorrection(MobileRecentCollection row)
    {
        ServeDirect();
        return RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today).Add(x => x.Correction, row));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Weight_measure_edit_uses_reviewed_kilograms_and_does_not_claim_success_when_stale(bool stale)
    {
        ServeFishVendor();
        const decimal rate = 22m;
        _api.Setup(x => x.QuoteOfficeCollectionAsync(It.IsAny<SourceNativeCollectionRequest>())).ReturnsAsync((SourceNativeCollectionRequest r) =>
            Result<SourceNativeChargeQuote>.Success(new(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", "Meat",
                RevenueInstrumentType.OfficialReceipt, rate * r.Charge!.Kilograms!.Value, "rate-version", r.Charge, Rate: rate)));
        RecordMobileCollectionEditRequest? sent = null;
        _api.Setup(x => x.QuoteCollectionEditAsync(It.IsAny<EditMobileCollectionIntent>())).ReturnsAsync((EditMobileCollectionIntent i) =>
            Result<CollectionSessionQuote>.Success(new(i.Replacement.ClientCollectionSessionId, null, Today,
                [new(i.Replacement.Items[0].ClientItemId, CollectionSessionItemKind.SourceNative, CollectorOperationCodes.WeightAndMeasure,
                    "Weight & Measure", "Meat", RevenueInstrumentType.OfficialReceipt, 44m, "rate-version", Guid.NewGuid())],
                [new(RevenueInstrumentType.OfficialReceipt, 44m)], 44m, "reviewed-edit", [])));
        _api.Setup(x => x.EditCollectionAsync(It.IsAny<RecordMobileCollectionEditRequest>())).Callback<RecordMobileCollectionEditRequest>(x => sent = x)
            .ReturnsAsync((RecordMobileCollectionEditRequest r) => stale
                ? Result<MobileCollectionCorrectionResult>.Failure("QuoteStale", ResultStatus.Conflict)
                : Result<MobileCollectionCorrectionResult>.Success(new(Guid.NewGuid(), r.Intent.CollectionId, "SRC-original",
                    new(Guid.NewGuid(), "SRC-replacement", RevenueInstrumentType.OfficialReceipt, 44m, [r.Intent.Replacement.Items[0].ClientItemId], "Posted"))));
        var row = RecentCollectionsTests.Recent("SRC-original", 66m, "Weight & Measure", "Pantom Dant", edit: true,
            source: new(FishVendor, CollectionSessionItemKind.SourceNative, CollectorOperationCodes.WeightAndMeasure,
                new(VendorRegistrationId: FishVendor.Id), new(CollectorOperationCodes.WeightAndMeasure, FishVendor.Id, Kilograms: 3m)));
        var view = OpenCorrection(row);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[role=dialog] input[type=number]")));
        view.Find("[role=dialog] input[type=number]").Input("2");
        view.WaitForAssertion(() => Assert.False(AddDisabled(view)));
        Click(view, "Add item"); view.FindAll(".cr-option").First().Click(); Click(view, "Review collection");
        view.WaitForAssertion(() => Assert.False(view.FindAll("button").Single(x => x.TextContent.Trim() == "Save changes").HasAttribute("disabled")));
        Click(view, "Save changes");
        view.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal(2m, sent!.Intent.Replacement.Items[0].Native!.Kilograms);
        Assert.Equal(FishVendor, sent.Intent.Replacement.SourceIdentity); Assert.Equal("reviewed-edit", sent.QuoteFingerprint);
        if (stale)
        {
            view.WaitForAssertion(() => Assert.Contains("The correction could not be completed. Check today's recent collections before trying again.", view.Find(".collection-message[role=status]").TextContent));
            Assert.DoesNotContain("Collection corrected", view.Markup); Assert.DoesNotContain("SRC-replacement", view.Markup);
            Assert.Equal(66m, row.Collection.NetAmount);
        }
        else
        {
            view.WaitForAssertion(() => Assert.Contains("Collection corrected", view.Markup));
            Assert.Contains("SRC-replacement", view.Markup); Assert.Contains("SRC-original stays on record", view.Markup);
        }
        Assert.Empty(_queue);
    }

    [Fact]
    public void Editing_a_recorded_terminal_collection_opens_the_form_on_what_was_recorded()
    {
        var view = OpenCorrection(TerminalRecent());

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[role=dialog]")));
        var sheet = view.Find("[role=dialog]");
        Assert.Equal("TRICYCAD", view.FindAll("button[role=radio][aria-checked=true]").Single().TextContent.Trim());
        Assert.Equal("3200", sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "0.01").GetAttribute("value"));
        Assert.Single(sheet.QuerySelectorAll("input[type=number]"));
        Assert.DoesNotContain("Cash Ticket count", sheet.TextContent);
        Assert.Contains("Change", view.FindAll("button").First(b => b.TextContent.Trim() == "Change").TextContent);   // the source is fixed while correcting
    }

    [Fact]
    public void A_correction_needs_a_reason_then_quotes_and_saves_with_one_stable_operation_and_shows_both_srcs()
    {
        EditMobileCollectionIntent? quoted = null; RecordMobileCollectionEditRequest? saved = null;
        var row = TerminalRecent();
        _api.Setup(x => x.QuoteCollectionEditAsync(It.IsAny<EditMobileCollectionIntent>())).Callback<EditMobileCollectionIntent>(i => quoted = i).ReturnsAsync((EditMobileCollectionIntent i) =>
            Result<CollectionSessionQuote>.Success(new(i.Replacement.ClientCollectionSessionId, null, Today,
                i.Replacement.Items.Select(x => new CollectionSessionItemQuote(x.ClientItemId, x.Kind, "TERMINAL", "TRICYCAD", "Section total", RevenueInstrumentType.CashTicket, x.ConfirmedAmount, "v", Guid.NewGuid())).ToArray(),
                [new(RevenueInstrumentType.CashTicket, i.Replacement.Items.Sum(x => x.ConfirmedAmount))], i.Replacement.Items.Sum(x => x.ConfirmedAmount), "fp-1", [])));
        _api.Setup(x => x.EditCollectionAsync(It.IsAny<RecordMobileCollectionEditRequest>())).Callback<RecordMobileCollectionEditRequest>(r => saved = r).ReturnsAsync((RecordMobileCollectionEditRequest r) =>
            Result<MobileCollectionCorrectionResult>.Success(new(Guid.NewGuid(), r.Intent.CollectionId, "SRC-2026-000022",
                new(Guid.NewGuid(), "SRC-2026-000031", RevenueInstrumentType.CashTicket, 3300m, r.Intent.Replacement.Items.Select(x => x.ClientItemId).ToArray(), "Posted"))));
        var view = OpenCorrection(row);
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[role=dialog]")));
        view.Find("[role=dialog]").QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("3300");
        Click(view, "Add item");

        Click(view, "Review collection");                                                                              // no reason yet
        Assert.Contains("Choose a reason first", view.Markup);
        _api.Verify(x => x.QuoteCollectionEditAsync(It.IsAny<EditMobileCollectionIntent>()), Times.Never);

        view.FindAll(".cr-option").Single(o => o.TextContent.Contains("Wrong amount")).Click();
        Click(view, "Review collection");
        view.WaitForAssertion(() => Assert.NotNull(quoted));
        Assert.Equal(quoted!.ClientOperationId, quoted.Replacement.ClientCollectionSessionId);                           // the server requires the same ID on both
        Assert.Equal(row.Collection.CollectionId, quoted.CollectionId);
        Assert.Equal(MobileCorrectionReason.WrongAmount, quoted.Reason.Code);
        Assert.Equal(Today, quoted.Replacement.BusinessDate);
        Assert.Equal(3300m, Assert.Single(quoted.Replacement.Items).ConfirmedAmount);

        Click(view, "Save changes");

        view.WaitForAssertion(() => Assert.Contains("Collection corrected", view.Markup));
        Assert.Equal("fp-1", saved!.QuoteFingerprint);
        Assert.Equal(quoted.ClientOperationId, saved.Intent.ClientOperationId);                                           // unchanged between quote and confirm
        Assert.Contains("SRC-2026-000022 stays on record", view.Markup);
        Assert.Contains("SRC-2026-000031", view.Markup);                                                                   // and the replacement's own SRC
        Assert.Empty(_queue);                                                                                              // a correction is made online, never queued
        _api.Verify(x => x.QuoteCollectionSessionAsync(It.IsAny<CollectionSessionIntent>()), Times.Never);
    }

    [Fact]
    public void A_collection_the_form_has_no_entry_for_says_so_and_offers_nothing_wrong()
    {
        var row = RecentCollectionsTests.Recent("SRC-2026-000020", 500m, "Water Consumption", edit: true);   // no resolved edit source

        var view = OpenCorrection(row);

        view.WaitForAssertion(() => Assert.Contains("can't be edited here", view.Markup));
        Assert.Empty(view.FindAll("[role=dialog]"));
    }

    [Fact]
    public void A_changed_reason_after_a_quote_is_a_different_proposed_correction_with_a_fresh_operation()
    {
        var ids = new List<Guid>();
        _api.Setup(x => x.QuoteCollectionEditAsync(It.IsAny<EditMobileCollectionIntent>())).Callback<EditMobileCollectionIntent>(i => ids.Add(i.ClientOperationId)).ReturnsAsync((EditMobileCollectionIntent i) =>
            Result<CollectionSessionQuote>.Success(new(i.Replacement.ClientCollectionSessionId, null, Today,
                i.Replacement.Items.Select(x => new CollectionSessionItemQuote(x.ClientItemId, x.Kind, "TERMINAL", "TRICYCAD", "", RevenueInstrumentType.CashTicket, x.ConfirmedAmount, "v", Guid.NewGuid())).ToArray(),
                [], 3200m, "fp", [])));
        var view = OpenCorrection(TerminalRecent());
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[role=dialog]")));
        Click(view, "Add item");
        view.FindAll(".cr-option").First().Click(); Click(view, "Review collection");
        view.WaitForAssertion(() => Assert.Single(ids));

        view.FindAll(".cr-option").Single(o => o.TextContent.Contains("Duplicate")).Click(); Click(view, "Review collection");

        view.WaitForAssertion(() => Assert.Equal(2, ids.Count));
        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public void A_stall_that_has_paid_today_is_not_offered_the_daily_payment_but_whole_payment_follows_its_own_eligibility()
    {
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [new(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", true, true, null, null, false, [], true,
                [new("Whole|stall", CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", "Stall 2 · October", new(StallId: DailyStall, OccupancyId: Guid.NewGuid(), Year: 2026, Month: 10),
                    RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance, 870m, RequiredInputs: [])], CollectionFamily.Rent)],
            SourceIdentity: Occupancy)));
        var view = PickSource("Ana Reyes");

        Click(view, "+ Add item");

        var rows = view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Select(x => x.TextContent.Replace("›", "").Trim()).ToList();
        Assert.DoesNotContain(rows, r => r.Contains("Daily"));
        Assert.Contains(rows, r => r.StartsWith("Whole payment") && r.Contains("₱870.00 remaining"));
    }

    [Fact]
    public void The_daily_amount_shown_is_whatever_the_server_returned_never_a_built_in_rate()
    {
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [new(CollectionSessionItemKind.NpmDaily, "NPM_DAILY", "Daily stall payment", true, true, null, null, false, [], true,
                [new("Daily|stall", CollectionSessionItemKind.NpmDaily, "NPM_DAILY", "Daily stall payment", "Stall 2", new(StallId: DailyStall, OccupancyId: Guid.NewGuid(), PeriodStart: Today),
                    RevenueInstrumentType.CashTicket, CollectionSessionAmountRule.FixedAmount, 47.5m, RequiredInputs: [])], CollectionFamily.Rent)],
            SourceIdentity: Occupancy)));
        var view = PickSource("Ana Reyes");

        Click(view, "+ Add item");

        var daily = view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single().TextContent;
        Assert.Contains("₱47.50", daily);
        Assert.DoesNotContain("₱30", daily);
    }

    // ── Recent utility collections before another payment ──

    private static readonly Guid EcfStall = Guid.NewGuid();
    private static readonly Guid EcfBill = Guid.NewGuid();

    private void ServeEcfSource() => _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Occupancy)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
        [Op(CollectionSessionItemKind.Electricity, "ECF", "Electricity", CollectionFamily.Market,
            new CollectionSessionSourceChoice("ECF|stall", CollectionSessionItemKind.Electricity, "ECF", "Electricity", "New Public Market · Stall 1",
                new(StallId: EcfStall, UtilityBillId: EcfBill, OccupancyId: Occupancy.Id, Year: Today.Year, Month: Today.Month),
                RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.DirectAmount, RequiredInputs: ["AmountReceived"]))],
        SourceIdentity: Occupancy)));

    private void ServeRecent(params RecentUtilityCollectionDto[] items) => _api
        .Setup(x => x.GetRecentUtilityCollectionsAsync(EcfStall, "ECF", Today.Year, Today.Month))
        .ReturnsAsync(Result<IReadOnlyList<RecentUtilityCollectionDto>>.Success(items));

    private static RecentUtilityCollectionDto Recent(decimal amount, string src, string disposition = "Posted") =>
        new(Guid.NewGuid(), src, amount, Today, DateTime.UtcNow.AddMinutes(-20), disposition, "Collector One");

    private IRenderedComponent<CollectionSessionEditor> OpenEcfChoice(string amount = "100")
    {
        ServeEcfSource();
        var view = PickSource("Ana Reyes");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option").Single(b => b.TextContent.Contains("Electricity")).Click();
        view.Find("[role=dialog] input[type=number]").Input(amount);
        return view;
    }

    [Fact]
    public void A_prior_ECF_collection_is_shown_with_its_SRC_and_another_payment_needs_an_explicit_confirmation()
    {
        ServeRecent(Recent(656m, "SRC-2026-000016"));
        var view = OpenEcfChoice();

        view.WaitForAssertion(() =>
        {
            var sheet = view.Find("[role=dialog]");
            Assert.Contains("Already collected this period", sheet.TextContent);
            Assert.Contains("A collection has already been recorded for this source and period.", sheet.TextContent);
            Assert.Contains("₱656.00", sheet.TextContent);
            Assert.Contains("SRC-2026-000016", sheet.TextContent);
        });

        Click(view, "Add item");                                          // first tap asks; it never silently adds
        Assert.Contains("Record another payment?", view.Find("[role=dialog]").TextContent);
        Assert.Empty(view.FindAll(".item-row"));

        Click(view, "Continue");                                          // the Collector confirms a separate payment was received
        Assert.Single(view.FindAll(".item-row"));
    }

    [Fact]
    public void Cancelling_the_repeat_confirmation_adds_nothing()
    {
        ServeRecent(Recent(656m, "SRC-2026-000016"));
        var view = OpenEcfChoice();
        view.WaitForAssertion(() => Assert.Contains("SRC-2026-000016", view.Find("[role=dialog]").TextContent));

        Click(view, "Add item");
        Click(view, "Cancel");

        Assert.Empty(view.FindAll(".item-row"));
        Assert.DoesNotContain("Record another payment?", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void A_reversed_collection_is_not_shown_as_valid_and_does_not_ask_for_confirmation()
    {
        ServeRecent(Recent(656m, "SRC-2026-000016", "Reversed"));
        var view = OpenEcfChoice();

        view.WaitForAssertion(() =>
        {
            var sheet = view.Find("[role=dialog]");
            Assert.Contains("Earlier collection reversed", sheet.TextContent);
            Assert.Contains("Reversed", sheet.QuerySelector(".recent-tag")!.TextContent);
            Assert.DoesNotContain("Already collected this period", sheet.TextContent);
        });
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));                         // nothing to confirm: no valid collection exists
    }

    [Fact]
    public void An_unverifiable_history_says_so_never_claims_nothing_was_recorded_and_does_not_block_the_server_decided_post()
    {
        _api.Setup(x => x.GetRecentUtilityCollectionsAsync(EcfStall, "ECF", Today.Year, Today.Month))
            .ReturnsAsync(Result<IReadOnlyList<RecentUtilityCollectionDto>>.Failure("Offline"));
        var view = OpenEcfChoice();

        view.WaitForAssertion(() => Assert.Contains("Recent collections not checked", view.Find("[role=dialog]").TextContent));
        Assert.DoesNotContain("No collection", view.Find("[role=dialog]").TextContent);
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));
    }

    [Fact]
    public void A_charge_already_in_the_collection_is_not_offered_again()
    {
        ServeRecent();
        var view = OpenEcfChoice();
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));

        // What is already charged is no longer offered: the same source and period cannot be added twice.
        Click(view, "+ Add item");

        Assert.Empty(view.FindAll("[role=dialog]"));
        Assert.Single(view.FindAll(".item-row"));
        Assert.Contains("already in this collection", view.Markup);
    }

    [Fact]
    public void A_vendors_linked_stall_utilities_are_listed_under_Utilities_in_the_same_checkout()
    {
        ServeFishVendor();
        var linked = new CollectionSourceSearchResult(Occupancy, "Ana Reyes", "New Public Market · Stall 1", "FACILITY_NPM", Utilities: ["ECF"]);
        ServeEcfSource();
        ServeRecent();
        var served = _api.Object.GetSourceCollectionDiscoveryAsync(FishVendor).GetAwaiter().GetResult().Value!;
        var stall = _api.Object.GetSourceCollectionDiscoveryAsync(Occupancy).GetAwaiter().GetResult().Value!;
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(FishVendor)).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(
            served with { LinkedSources = [linked], Operations = [.. served.Operations, .. stall.Operations] }));
        var view = PickSource("Pantom Dant");

        Click(view, "+ Add item");
        var sheet = view.Find("[role=dialog]");
        Assert.Contains("Income from Market", sheet.TextContent);                         // the vendor's own two fees stay where they were
        Assert.Contains("Fish / Meat Vendor Fee", sheet.TextContent);
        Assert.Contains("Utilities", sheet.TextContent);
        Assert.Contains("Electricity", sheet.TextContent);
        Assert.DoesNotContain("Water", sheet.TextContent);                                // only what the stall really offers
        Assert.DoesNotContain("separate payment", sheet.TextContent);

        sheet.QuerySelectorAll(".collection-option").Single(b => b.TextContent.Contains("Electricity")).Click();

        view.WaitForAssertion(() =>
        {
            Assert.Equal("Pantom Dant", view.Find(".payer-selected strong").TextContent.Trim());     // no source switch: the vendor's own checkout
            Assert.Contains("Electricity", view.Find("[role=dialog]").TextContent);
        }, TimeSpan.FromSeconds(3));
        Assert.Empty(_queue);
    }
    [Fact]
    public void A_vendor_without_a_linked_stall_utility_shows_no_Utilities_group()
    {
        ServeFishVendor();
        var view = PickSource("Pantom Dant");

        Click(view, "+ Add item");

        Assert.DoesNotContain("Utilities", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void Verified_fine_prefills_assessed_balance_and_preserves_receipt_and_liability_identity()
    {
        var assessment = Guid.NewGuid();
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(Kanmanggay))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
                [Op(CollectionSessionItemKind.FineAssessment, RevenueClassificationCodes.PenaltiesAndFines,
                    "Fines / Penalties", CollectionFamily.Space,
                    new CollectionSessionSourceChoice("FINE|" + assessment.ToString("N"),
                        CollectionSessionItemKind.FineAssessment, RevenueClassificationCodes.PenaltiesAndFines,
                        "Approved violation", "Kanmanggay · Space 4 · Outstanding assessment",
                        new(FineAssessmentId: assessment, FineVersion: 3),
                        RevenueInstrumentType.OfficialReceipt, CollectionSessionAmountRule.PreparedBalance,
                        ServerAmount: 150m, MaximumAmount: 150m))],
                SourceIdentity: Kanmanggay)));

        var view = PickSource("Juan Cruz");
        Click(view, "+ Add item");
        view.Find("[role=dialog]").QuerySelectorAll(".collection-option")
            .Single(x => x.TextContent.Contains("Fines / Penalties")).Click();
        var sheet = view.Find("[role=dialog]");
        Assert.Contains("Outstanding assessed fine", sheet.TextContent);
        Assert.Contains("Official Receipt", sheet.TextContent);
        Assert.Equal("150", Assert.Single(sheet.QuerySelectorAll("input[type=number]")).GetAttribute("value"));
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));
        Assert.Contains("150", view.Find(".item-row").TextContent);

        Click(view, "Review collection");
        _api.Verify(x => x.QuoteCollectionSessionAsync(It.Is<CollectionSessionIntent>(intent =>
            intent.SourceIdentity == Kanmanggay && intent.Items.Count == 1 &&
            intent.Items[0].Kind == CollectionSessionItemKind.FineAssessment &&
            intent.Items[0].Fine != null &&
            intent.Items[0].Fine!.AssessmentId == assessment &&
            intent.Items[0].Fine!.Version == 3 &&
            intent.Items[0].ConfirmedAmount == 150m)), Times.Once);
    }

    [Fact]
    public void A_utility_item_cannot_be_added_until_the_earlier_collections_check_has_answered()
    {
        var gate = new TaskCompletionSource<Result<IReadOnlyList<RecentUtilityCollectionDto>>>();
        _api.Setup(x => x.GetRecentUtilityCollectionsAsync(EcfStall, "ECF", Today.Year, Today.Month)).Returns(gate.Task);
        var view = OpenEcfChoice();

        view.WaitForAssertion(() => Assert.Contains("Checking earlier collections", view.Find("[role=dialog]").TextContent));
        Assert.True(AddDisabled(view));                                                           // a tap now would bypass the duplicate-awareness
        Assert.Empty(view.FindAll(".item-row"));

        gate.SetResult(Result<IReadOnlyList<RecentUtilityCollectionDto>>.Success([]));
        view.WaitForAssertion(() => Assert.False(AddDisabled(view)));
        Click(view, "Add item");
        Assert.Single(view.FindAll(".item-row"));
    }
}
