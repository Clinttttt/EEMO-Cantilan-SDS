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
    [Fact]
    public void Whole_payment_summary_uses_server_monthly_balance_without_days_covered()
    {
        var view = RenderComponent<NpmWholeSummary>(p => p.Add(x => x.Quote,
            new NpmWholePaymentQuoteDto(_stall, 2026, 2, Today, 28, 600m, 0m, "reviewed",
                RevenueInstrumentType.OfficialReceipt, MonthlyObligation: 900m, Collected: 300m)));
        Assert.Contains("February 2026", view.Markup);
        Assert.Contains("Monthly obligation", view.Markup);
        Assert.Contains("₱900.00", view.Markup);
        Assert.Contains("₱300.00", view.Markup);
        Assert.Contains("Remaining", view.Markup);
        Assert.Contains("₱600.00", view.Markup);
        Assert.Contains("Official Receipt", view.Markup);
        Assert.DoesNotContain("Days covered", view.Markup);
    }

    [Fact]
    public void Direct_vendor_fee_has_vendor_amount_and_receipt_without_monthly_account_language()
    {
        _api.Setup(x => x.GetDirectVendorFeeSourcesAsync()).ReturnsAsync(
            Result<IReadOnlyList<DirectVendorFeeSource>>.Success([new(_stall, "12", "Fish section", _payer, "Lisa Ilogans", true, null)]));
        var view = RenderComponent<VendorFeeEntry>(p => p.Add(x => x.BusinessDate, Today));
        view.FindAll("button").Single(x => x.TextContent.Contains("Lisa Ilogans")).Click();
        Assert.Contains("Collect vendor fee", view.Markup);
        Assert.Contains("Amount received", view.Markup);
        Assert.Contains("Official Receipt", view.Markup);
        Assert.Single(view.FindAll("input[type=number]"));
        foreach (var phrase in new[] { "Monthly vendor fee", "Vendor fees due", "Monthly fee", "Balance", "Nothing due", "Days covered" })
            Assert.DoesNotContain(phrase, view.Markup);
        Assert.DoesNotContain("OR number", view.Markup);
    }

    [Fact]
    public void Direct_vendor_fee_empty_state_describes_vendors_rather_than_a_due_balance()
    {
        _api.Setup(x => x.GetDirectVendorFeeSourcesAsync()).ReturnsAsync(
            Result<IReadOnlyList<DirectVendorFeeSource>>.Success([]));
        var view = RenderComponent<VendorFeeEntry>(p => p.Add(x => x.BusinessDate, Today));
        Assert.Contains("No Fish/Meat vendors available.", view.Markup);
        Assert.DoesNotContain("Nothing due", view.Markup);
    }

    [Fact]
    public void Direct_vendor_fee_unavailable_vendor_is_not_tappable_and_shows_the_server_reason()
    {
        _api.Setup(x => x.GetDirectVendorFeeSourcesAsync()).ReturnsAsync(
            Result<IReadOnlyList<DirectVendorFeeSource>>.Success([
                new(_stall, "12", "Fish section", _payer, "Lisa Ilogans", true, null),
                new(Guid.NewGuid(), "14", "Meat section", null, "Ben Tan", false, "Needs Business Payor")]));
        var view = RenderComponent<VendorFeeEntry>(p => p.Add(x => x.BusinessDate, Today));
        Assert.DoesNotContain(view.FindAll("button"), x => x.TextContent.Contains("Ben Tan"));
        var row = view.Find(".vendor-row-unavailable");
        Assert.Contains("Needs Business Payor", row.TextContent);
        Assert.DoesNotContain("›", row.TextContent);
        Assert.Contains(view.FindAll("button.vendor-row"), x => x.TextContent.Contains("Lisa Ilogans"));
    }

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
    // A button is found by its label and pressed. A search that finishes in the background can re-render between the find and the press,
    // so a stale handler is retried against the fresh markup rather than failing the test.
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
    private static void ClickPayer(IRenderedComponent<CollectionSessionEditor> view, string name)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = view.FindAll(".payer-option").FirstOrDefault(x => x.QuerySelector("strong")!.TextContent.Trim() == name)
                ?? throw new InvalidOperationException($"No payer \"{name}\" in: " + view.Markup);
            try { row.Click(); return; }
            catch (Bunit.Rendering.UnknownEventHandlerIdException) when (attempt < 5) { Thread.Sleep(50); }
        }
    }
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
        var view = Open(); view.Find("input").Input("Lisa"); Click(view, "Search"); ClickPayer(view, "Lisa Ilogans"); view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));
        AddLanding(view);
        Click(view, "+ Add item"); Click(view, "Electricity ›");
        view.Find("input[type=number]").Input("44"); Click(view, "Add item"); Click(view, "Review collection");
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
    public void Review_summary_is_structured_total_strongest_and_has_no_serial_input()
    {
        var view = Open(); AddLanding(view); Click(view, "Review collection");

        var rows = view.FindAll(".review-summary .review-row");
        Assert.Equal(new[] { "Cash Ticket", "Total" }, rows.Select(r => r.QuerySelector("span")!.TextContent.Trim()).ToArray());
        Assert.Contains("review-total", rows[^1].ClassName);
        Assert.Equal("₱200.00", rows[^1].QuerySelector("strong")!.TextContent.Trim());
        Assert.Empty(view.FindAll(".review-problem"));
        Assert.Empty(view.FindAll("input[type=text]"));                           // a canonical collection asks for no typed document number
        Assert.DoesNotContain("Official receipt no", view.Markup, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Recorded_notice_shows_the_server_reference_prominently_and_never_makes_one_up()
    {
        var recorded = RenderComponent<RecordedNotice>(p => p.Add(x => x.Message, "Collection recorded · SRC-2026-000123"));
        Assert.Equal("SRC-2026-000123", recorded.Find(".recorded-ref").TextContent.Trim());
        Assert.Equal("Collection recorded", recorded.Find(".recorded-title").TextContent.Trim());

        var waiting = RenderComponent<RecordedNotice>(p => p.Add(x => x.Message, "Saved on this device. Waiting to sync."));
        Assert.Empty(waiting.FindAll(".recorded-ref"));
        Assert.DoesNotContain("SRC-", waiting.Markup);

        Assert.Empty(RenderComponent<RecordedNotice>(p => p.Add(x => x.Message, null)).FindAll(".state-card"));
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
    [Fact]
    public void Tapping_the_payer_field_browses_at_once_and_only_a_genuine_empty_answer_says_no_payer_found()
    {
        _api.Setup(x => x.SearchCollectionSessionPayorsAsync("")).ReturnsAsync(Result<IReadOnlyList<CollectionPayorDto>>.Success([]));
        var view = Open();
        Assert.Empty(view.FindAll(".payer-panel"));

        view.Find("input").Focus();

        view.WaitForAssertion(() => Assert.Contains("No payer found.", view.Find(".payer-panel").TextContent));
        _api.Verify(x => x.SearchCollectionSessionPayorsAsync(""), Times.Once);
        Assert.DoesNotContain("2 letters", view.Markup);                              // the old minimum-length rule is gone
    }
    [Fact]
    public void A_one_character_search_is_sent_and_each_result_shows_the_servers_context()
    {
        _api.Setup(x => x.SearchCollectionSessionPayorsAsync("L")).ReturnsAsync(Result<IReadOnlyList<CollectionPayorDto>>.Success(
            [new(_payer, "Lisa Ilogans", ["NPM · 12", "Fish area"])]));
        var view = Open();

        view.Find("input").Input("L");

        view.WaitForAssertion(() =>
        {
            var row = Assert.Single(view.FindAll(".payer-option"));
            Assert.Equal("Lisa Ilogans", row.QuerySelector("strong")!.TextContent.Trim());
            Assert.Equal("NPM · 12 · Fish area", row.QuerySelector("small")!.TextContent.Trim());
        });
    }
    [Fact]
    public void A_single_valid_source_is_already_chosen_so_the_sheet_shows_no_select_and_asks_only_for_the_amount()
    {
        var view = Open(); view.Find("input").Input("Lisa"); Click(view, "Search"); ClickPayer(view, "Lisa Ilogans");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));

        Click(view, "+ Add item"); Click(view, "Electricity ›");

        var sheet = view.Find("[role=dialog]");
        Assert.Empty(sheet.QuerySelectorAll(".mc-trigger"));
        Assert.DoesNotContain("Select", string.Join(" ", sheet.QuerySelectorAll("button").Select(b => b.TextContent.Trim())));
        Assert.Contains("12", sheet.QuerySelector(".sheet-source")!.TextContent);
        Assert.True(sheet.QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled"));
        sheet.QuerySelector("input[type=number]")!.Input("44");
        Assert.False(view.Find("[role=dialog]").QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled"));
    }
    private void ServeWeighing(bool withRates)
    {
        _api.Setup(x => x.GetCollectionSessionDiscoveryAsync(It.IsAny<Guid?>())).ReturnsAsync((Guid? id) => Result<CollectionSessionDiscovery>.Success(new(id, Today,
            [new(CollectionSessionItemKind.Weighing, "WEIGHT_AND_MEASURE", "Weight & Measure", true, true, null, null, true, [])],
            WeighingSources: [new(_stall, "1", id, "Pantom Dant", "New Public Market")],
            WeighingRates: withRates ? [new(WeighingType.Fish, 1m, Today), new(WeighingType.Meat, 66m, Today)] : [])));
    }
    private IRenderedComponent<CollectionSessionEditor> OpenWeighing()
    {
        var view = Open(); view.Find("input").Input("Lisa"); Click(view, "Search"); ClickPayer(view, "Lisa Ilogans");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));
        Click(view, "+ Add item"); Click(view, "Weight & Measure ›");
        return view;
    }
    private static bool AddDisabled(IRenderedComponent<CollectionSessionEditor> view) =>
        view.Find("[role=dialog]").QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled");

    [Fact]
    public void Weight_and_measure_enables_Add_item_as_the_quantity_is_typed_without_leaving_the_field()
    {
        ServeWeighing(withRates: true);
        var view = OpenWeighing();
        view.FindAll("button[role=option]").First(b => b.TextContent.Contains("Meat")).Click();
        Assert.True(AddDisabled(view));                                                           // no quantity yet

        view.Find("[role=dialog] input[type=number]").Input("3");                                  // typed, never blurred

        Assert.False(AddDisabled(view));
        Assert.Contains("₱66.00 / kg", view.Find("[role=dialog]").TextContent);                    // the rate stays the server's, shown read-only
    }

    [Fact]
    public void Weight_and_measure_stays_disabled_for_a_zero_or_empty_quantity()
    {
        ServeWeighing(withRates: true);
        var view = OpenWeighing();
        view.FindAll("button[role=option]").First(b => b.TextContent.Contains("Meat")).Click();

        view.Find("[role=dialog] input[type=number]").Input("0");
        Assert.True(AddDisabled(view));
        view.Find("[role=dialog] input[type=number]").Input("2.5");
        Assert.False(AddDisabled(view));
        view.Find("[role=dialog] input[type=number]").Input("");
        Assert.True(AddDisabled(view));
    }

    [Fact]
    public void Weight_and_measure_without_a_server_rate_can_never_be_added()
    {
        ServeWeighing(withRates: false);
        var view = OpenWeighing();

        view.Find("[role=dialog] input[type=number]").Input("3");

        Assert.True(AddDisabled(view));
        Assert.Contains("Unavailable", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void Transportation_offers_vehicle_class_and_quick_amount_and_quick_needs_only_the_amount()
    {
        _api.Setup(x => x.GetCollectionSessionDiscoveryAsync(It.IsAny<Guid?>())).ReturnsAsync((Guid? id) => Result<CollectionSessionDiscovery>.Success(new(id, Today,
            [new(CollectionSessionItemKind.GovernedService, "TRANSPORTATION", "Transportation / Parking", true, true, null, null, false, [])],
            ServiceTerms:
            [
                new(null, new("TRANSPORTATION", "Transportation / Parking", false, GovernedServiceBasis.VehicleClassRate, null, null, RevenueInstrumentType.CashTicket, false,
                    [new("JN", "Jeepney", 20m), new("VAN", "Van", 30m)])),
                new(GovernedServiceMode.QuickAmount, new("TRANSPORTATION", "Transportation / Parking", false, GovernedServiceBasis.DirectApprovedAmount, null, null, RevenueInstrumentType.CashTicket, false))
            ])));
        var view = Open(); view.Find("input").Input("Lisa"); Click(view, "Search"); ClickPayer(view, "Lisa Ilogans");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));
        Click(view, "+ Add item"); Click(view, "Transportation / Parking ›");
        var sheet = view.Find("[role=dialog]");

        Assert.Contains("Collection method", sheet.TextContent);
        Assert.Contains("By vehicle class", sheet.TextContent);
        Assert.Contains("Quick amount", sheet.TextContent);
        Assert.DoesNotContain("QuickAmount", view.Markup);                                          // no enum names reach the collector
        Assert.DoesNotContain("ceiling", view.Markup, StringComparison.OrdinalIgnoreCase);

        view.FindAll("button[role=option]").First(b => b.TextContent.Contains("Quick amount")).Click();
        sheet = view.Find("[role=dialog]");
        Assert.DoesNotContain("Vehicle class", sheet.TextContent);                                    // no class, no rate, no vehicle count
        Assert.True(AddDisabled(view));
        sheet.QuerySelector("input[type=number]")!.Input("85");
        Assert.False(AddDisabled(view));
    }

    private void ServeSources(Guid[] vendorStalls)
    {
        _api.Setup(x => x.GetCollectionSessionDiscoveryAsync(It.IsAny<Guid?>())).ReturnsAsync((Guid? id) => Result<CollectionSessionDiscovery>.Success(new(id, Today,
            [new(CollectionSessionItemKind.NpmWholePayment, "NPM_WHOLE_PAYMENT", "NPM Whole payment", true, true, null, null, true, []),
             new(CollectionSessionItemKind.VendorFee, "FISH_MEAT_VENDOR_FEE", "Fish / Meat Vendor Fee", true, true, null, null, true, [])],
            NpmSources: [new(_stall, "1", id, "Lisa Ilogans", "New Public Market")],
            VendorFeeSources: vendorStalls.Select((s, n) => new DirectVendorFeeSource(s, (n + 1).ToString(), "Fish area", id, "Lisa Ilogans", true, null)).ToArray())));
    }
    [Fact]
    public void NPM_whole_payment_with_one_stall_shows_its_context_a_year_and_a_month_choice_and_no_source_select()
    {
        ServeSources([]);
        var view = Open(); view.Find("input").Input("Lisa"); Click(view, "Search"); ClickPayer(view, "Lisa Ilogans");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));

        Click(view, "+ Add item"); Click(view, "NPM Whole payment ›");

        var sheet = view.Find("[role=dialog]");
        Assert.Contains("New Public Market", sheet.QuerySelector(".sheet-source")!.TextContent);
        Assert.Equal(new[] { "Month" }, sheet.QuerySelectorAll(".mc-trigger").Select(x => x.ParentElement!.QuerySelector(".mc-label")!.TextContent.Trim()).ToArray());
        Assert.Empty(sheet.QuerySelectorAll("select"));
        Assert.False(sheet.QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled"));
        Click(view, "Add item");
        Assert.Contains("Lisa Ilogans · NPM Stall 1 · Oct 2026", view.Find(".item-context").TextContent);   // the item row is never empty before review
    }
    [Fact]
    public void Several_valid_sources_are_selectable_rows_in_the_sheet_not_a_dropdown()
    {
        ServeSources([Guid.NewGuid(), Guid.NewGuid()]);
        var view = Open(); view.Find("input").Input("Lisa"); Click(view, "Search"); ClickPayer(view, "Lisa Ilogans");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".payer-selected")));

        Click(view, "+ Add item"); Click(view, "Fish / Meat Vendor Fee ›");

        var sheet = view.Find("[role=dialog]");
        Assert.Empty(sheet.QuerySelectorAll(".mc-trigger"));
        Assert.Equal(2, sheet.QuerySelectorAll(".mc-option").Length);
        Assert.True(sheet.QuerySelectorAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Add item").HasAttribute("disabled"));
    }
    [Fact]
    public void The_operation_picker_groups_what_the_server_offers_without_offering_anything_else()
    {
        var view = Open();

        Click(view, "+ Add item");

        var groups = view.Find("[role=dialog]").QuerySelectorAll(".sheet-group").Select(x => x.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Transport & services" }, groups);                         // only Landing / Berthing is eligible in this discovery
    }
    [Fact]
    public void Suggestions_from_the_server_are_listed_in_a_scrolling_list_and_choosing_one_shows_the_payer_clearly()
    {
        _api.Setup(x => x.SearchCollectionSessionPayorsAsync("")).ReturnsAsync(Result<IReadOnlyList<CollectionPayorDto>>.Success(
            Enumerable.Range(1, 8).Select(n => new CollectionPayorDto(Guid.NewGuid(), $"Payer {n}")).ToArray()));
        var view = Open();

        view.Find("input").Focus();

        view.WaitForAssertion(() => Assert.Equal(8, view.FindAll(".payer-list .payer-option").Count));
        Assert.NotNull(view.Find(".payer-list"));                                   // one bounded list that scrolls inside itself
        ClickPayer(view, "Payer 3");
        view.WaitForAssertion(() =>
        {
            Assert.Equal("Payer 3", view.Find(".payer-selected .payer-name").TextContent.Trim());
            Assert.Empty(view.FindAll(".payer-panel"));
            Assert.Contains(view.FindAll("button"), b => b.TextContent.Trim() == "Change");
        });
    }
    [Fact]
    public void Typing_searches_and_a_search_with_no_result_says_so_only_afterwards()
    {
        _api.Setup(x => x.SearchCollectionSessionPayorsAsync("Zed")).ReturnsAsync(Result<IReadOnlyList<CollectionPayorDto>>.Success([]));
        var view = Open();

        view.Find("input").Input("Zed");
        view.Find("input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        view.WaitForAssertion(() => Assert.Contains("No payer found.", view.Find(".payer-panel").TextContent));
        _api.Verify(x => x.SearchCollectionSessionPayorsAsync("Zed"), Times.AtLeastOnce);
    }
    [Fact]
    public void Add_item_with_nothing_to_choose_explains_in_the_page_instead_of_opening_an_empty_sheet()
    {
        _api.Setup(x => x.GetCollectionSessionDiscoveryAsync(It.IsAny<Guid?>())).ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today,
            [new(CollectionSessionItemKind.Electricity, "ECF", "Electricity", true, false, null, null, true, [])])));
        var view = Open();

        Click(view, "+ Add item");

        Assert.Empty(view.FindAll("[role=dialog]"));
        Assert.Contains("Select a payer to see what can be collected.", view.Find(".collection-message").TextContent);
    }
    [Fact]
    public void The_add_item_sheet_has_a_head_a_scrolling_body_and_one_action_row()
    {
        var view = Open();

        Click(view, "+ Add item");

        var sheet = view.Find("[role=dialog]");
        Assert.NotNull(sheet.QuerySelector(".sheet-handle"));
        Assert.Equal("Add item", sheet.QuerySelector(".sheet-title")!.TextContent.Trim());
        Assert.NotNull(sheet.QuerySelector(".sheet-body"));
        Assert.Equal(new[] { "Close" }, sheet.QuerySelectorAll(".sheet-actions button").Select(b => b.TextContent.Trim()).ToArray());
        Click(view, "Landing / Berthing ›");
        Assert.Equal(new[] { "Close", "Add item" }, view.Find("[role=dialog]").QuerySelectorAll(".sheet-actions button").Select(b => b.TextContent.Trim()).ToArray());
    }
}
