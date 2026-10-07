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
    private readonly Connectivity _connection = new();
    private static readonly CollectionSourceIdentity FishVendor = new(SourceIdentityKind.FishMeatVendorRegistration, Guid.NewGuid());
    private static readonly CollectionSourceIdentity Occupancy = new(SourceIdentityKind.Occupancy, Guid.NewGuid());
    private static readonly CollectionSourceIdentity Kanmanggay = new(SourceIdentityKind.SpaceAccount, Guid.NewGuid());

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
        _api.Setup(x => x.SearchCollectionSourcesAsync(It.IsAny<string?>())).ReturnsAsync(Result<IReadOnlyList<CollectionSourceSearchResult>>.Success([
            new(FishVendor, "Pantom Dant", "Meat · 2026", CollectorOperationCodes.FishMeatVendorFee, 2026, FishMeatVendorType.Meat),
            new(Kanmanggay, "Juan Cruz", "Kanmanggay · Space 4", CollectorOperationCodes.KanmanggaySpaceRental),
            new(Occupancy, "Ana Reyes", "New Public Market · Stall 1", "FACILITY_NPM")]));
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
        SourceIdentity: FishVendor)));

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

    private IRenderedComponent<CollectionSessionEditor> Open(string? only = null) =>
        RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today).Add(x => x.OnlyOperation, only));

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
        view.Find("input[type=search]").Focus();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-option")));
        ClickSource(view, name);
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));
        return view;
    }

    private static bool AddDisabled(IRenderedComponent<CollectionSessionEditor> view) =>
        view.Find("[role=dialog]").QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled");

    [Fact]
    public void Whole_payment_summary_uses_server_monthly_balance_without_days_covered()
    {
        var view = RenderComponent<NpmWholeSummary>(p => p.Add(x => x.Quote,
            new NpmWholePaymentQuoteDto(Guid.NewGuid(), 2026, 2, Today, 28, 600m, 0m, "reviewed",
                RevenueInstrumentType.OfficialReceipt, MonthlyObligation: 900m, Collected: 300m)));
        Assert.Contains("February 2026", view.Markup);
        Assert.Contains("₱600.00", view.Markup);
        Assert.DoesNotContain("Days covered", view.Markup);
    }

    [Fact]
    public void Focusing_the_empty_search_browses_sources_each_row_showing_its_name_and_typed_context_and_never_an_id()
    {
        var view = Open();
        Assert.Empty(view.FindAll(".payer-panel"));

        view.Find("input[type=search]").Focus();

        view.WaitForAssertion(() =>
        {
            var rows = view.FindAll(".payer-option").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("Pantom Dant") && r.Contains("Meat · 2026"));
            Assert.Contains(rows, r => r.Contains("Juan Cruz") && r.Contains("Kanmanggay · Space 4"));
            Assert.Contains(rows, r => r.Contains("Ana Reyes") && r.Contains("New Public Market · Stall 1"));
            Assert.DoesNotContain("FishMeatVendorRegistration", view.Markup);
            Assert.DoesNotContain(FishVendor.Id.ToString(), view.Markup);
        });
        Assert.NotNull(view.Find(".payer-list"));                                       // bounded list that scrolls inside the panel
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
    public void Terminal_takes_a_section_a_direct_amount_and_an_optional_ticket_count_that_never_prices_the_amount()
    {
        ServeDirect();
        var view = Open("TERMINAL");
        view.WaitForAssertion(() => Assert.Contains("Income From Terminal", view.Find(".collection-payer h2").TextContent));
        Click(view, "+ Add item");
        var sheet = view.Find("[role=dialog]");
        foreach (var section in new[] { "COMFORT ROOM", "PULL PUL VANS, CARGO VANS", "TRICYCAD" }) Assert.Contains(section, sheet.TextContent);
        view.FindAll("button[role=radio]").First(b => b.TextContent.Contains("TRICYCAD")).Click();
        Assert.True(AddDisabled(view));

        sheet = view.Find("[role=dialog]");
        sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "0.01").Input("900");
        sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "1").Input("30");
        Assert.Contains("CT · Cash Ticket", view.Find("[role=dialog]").TextContent);
        Click(view, "Add item"); Click(view, "Review collection"); Click(view, "Record collection");

        view.WaitForAssertion(() => Assert.Contains("SRC-2026-000001", view.Markup));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((TerminalSection.Tricycad, 30, 900m), (item.Native!.Section, item.Native.CashTicketCount, item.ConfirmedAmount));
        Assert.Null(item.Native.VehicleClassId);                                                    // a section total names no vehicle
    }

    private IRenderedComponent<CollectionSessionEditor> OpenTerminalSheet()
    {
        ServeDirect();
        var view = Open("TERMINAL");
        view.WaitForAssertion(() => Assert.Contains("Income From Terminal", view.Find(".collection-payer h2").TextContent));
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
        sheet.QuerySelectorAll("input[type=number]").First(i => i.GetAttribute("step") == "1").Input("24");   // 24 × 20 is not 1,234.50 and must not change it
        Assert.False(AddDisabled(view));                                                               // the button reacts while typing
        Click(view, "Add item");

        var row = view.Find(".item-row");
        Assert.Contains("PULL PUL VANS, CARGO VANS", row.TextContent);
        Assert.Contains("Jeepney · ₱20.00 approved rate · 24 cash tickets", row.TextContent);
        Assert.DoesNotContain("Income From Terminal", row.TextContent);
        Click(view, "Review collection"); Click(view, "Record collection");
        view.WaitForAssertion(() => Assert.Single(_queue));
        var item = Assert.Single(_queue[0].CollectionSession!.Intent.Items);
        Assert.Equal((TerminalSection.PullPulVansCargoVans, Jeepney, 24, 1234.50m), (item.Native!.Section, item.Native.VehicleClassId, item.Native.CashTicketCount, item.ConfirmedAmount));
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
        Assert.True(view.FindAll("button").Single(b => b.TextContent.Trim() == "Review collection").HasAttribute("disabled"));
        Assert.True(view.FindAll("button").Single(b => b.TextContent.Trim() == "Record collection").HasAttribute("disabled"));
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
}
