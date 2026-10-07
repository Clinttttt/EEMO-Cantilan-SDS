using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>Today's recent collections: the server's list for this collector, only recorded facts, and Edit / Remove exactly as the server allows them.</summary>
public sealed class RecentCollectionsTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private readonly Mock<IMobileApiClient> _api = new();
    private readonly Connectivity _connection = new();

    private sealed class Connectivity : IConnectivityMonitor { public bool Online = true; public bool IsOnline => Online; public event Action? ConnectivityRestored { add { } remove { } } }

    internal static MobileRecentCollection Recent(string src, decimal amount, string line, string? payer = null, bool edit = false, bool remove = false, string? blockCode = null,
        string? block = null, decimal? net = null, SourceNativeActivityDto? native = null, MobileCollectionEditSource? source = null, RevenueInstrumentType instrument = RevenueInstrumentType.OfficialReceipt) =>
        new(new CollectionActivityEventDto("k-" + src, "Canonical", "Collection", Guid.NewGuid(), Today, new DateTime(2026, 10, 7, 3, 21, 0, DateTimeKind.Utc), src, null, instrument, [], [], payer,
                null, null, "Cora Collector", "cora", null, null, null, amount, 0m, net ?? amount, "Posted", null, [new CollectionActivityLineDto("X", line, amount, null, null, null, null, null, null)], []),
            edit, remove, blockCode, block, native, source);

    public RecentCollectionsTests()
    {
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("tenant:collector");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, _connection, owner.Object));
    }

    private IRenderedComponent<RecentCollections> Open(params MobileRecentCollection[] rows)
    {
        _api.Setup(x => x.GetRecentCollectionsAsync()).ReturnsAsync(Result<MobileRecentCollections>.Success(new(Today, rows, false)));
        var view = RenderComponent<RecentCollections>();
        view.WaitForAssertion(() => Assert.Empty(view.FindAll(".rc-note[role=status]")));
        return view;
    }

    [Fact]
    public void ListsTodaysCollectionsNewestFirst_WithOnlyTheFactsThatExist()
    {
        var terminal = new SourceNativeActivityDto(Guid.NewGuid(), "SRC-2026-000022", Today, DateTime.UtcNow, "TERMINAL", TerminalSection.Tricycad, null, null, null, "Cora", 3200m, 3200m, "Posted", 80, null, null, null, null);
        var view = Open(Recent("SRC-2026-000022", 3200m, "TRICYCAD", native: terminal, instrument: RevenueInstrumentType.CashTicket), Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", "Walk-up Vendor"));

        var rows = view.FindAll(".rc-row").Select(r => r.TextContent).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("SRC-2026-000022", rows[0]);                                                    // the server's order, newest first
        Assert.Contains("TRICYCAD", rows[0]); Assert.Contains("Section total", rows[0]); Assert.DoesNotContain("80 CT", rows[0]); Assert.Contains("₱3,200.00", rows[0]);
        Assert.Contains("Fish / Meat Vendor Fee", rows[1]); Assert.Contains("Walk-up Vendor", rows[1]); Assert.Contains("₱75.00", rows[1]);
        Assert.DoesNotContain("CT", rows[1].Replace("SRC", ""));                                         // no ticket count was recorded, so none is shown
    }

    [Fact]
    public void ADedicatedPageListsOnlyItsOwnOperation()
    {
        _api.Setup(x => x.GetRecentCollectionsAsync()).ReturnsAsync(Result<MobileRecentCollections>.Success(new(Today,
            [Recent("SRC-2026-000022", 3200m, "TRICYCAD", native: new(Guid.NewGuid(), "SRC-2026-000022", Today, DateTime.UtcNow, "TERMINAL", TerminalSection.Tricycad, null, null, null, "Cora", 3200m, 3200m, "Posted", null, null, null, null, null)),
             Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee")], false)));

        var view = RenderComponent<RecentCollections>(p => p.Add(x => x.OnlyOperation, "TERMINAL").Add(x => x.Heading, "Today's Terminal collections"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll(".rc-row")));
        Assert.Equal("Today's Terminal collections", view.Find(".rc-title").TextContent.Trim());
    }

    [Fact]
    public void AnEligibleCollectionOffersEditAndRemove_WithNoOfficeLecture()
    {
        var view = Open(Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", "Walk-up Vendor", edit: true, remove: true));

        view.Find(".rc-row").Click();

        var sheet = view.Find("[role=dialog]");
        Assert.Equal(["Close", "Remove", "Edit"], sheet.QuerySelectorAll(".sheet-actions button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.DoesNotContain("ask the office", sheet.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never edited", sheet.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SRC-2026-000021", sheet.TextContent);
    }

    [Fact]
    public void ABlockedCollectionShowsNoActiveButtons_JustTheServersReason()
    {
        var view = Open(Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", "Walk-up Vendor", blockCode: "Remitted", block: "Already remitted"));

        view.Find(".rc-row").Click();

        var sheet = view.Find("[role=dialog]");
        Assert.Equal(["Close"], sheet.QuerySelectorAll(".sheet-actions button").Select(b => b.TextContent.Trim()).ToArray());
        Assert.Contains("already remitted", sheet.QuerySelector(".rc-block")!.TextContent);   // worded from the code
    }

    [Fact]
    public void EditOpensTheCorrectionFormForThatCollection()
    {
        var row = Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", edit: true);
        var view = Open(row);
        view.Find(".rc-row").Click();

        view.FindAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Edit").Click();

        Assert.EndsWith($"/collections/new?correct={row.Collection.CollectionId}", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public void RemoveNeedsAReason_AndForOtherANote_ThenReversesWithAStableOperationAndRefreshes()
    {
        RemoveMobileCollectionRequest? sent = null;
        var row = Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", "Walk-up Vendor", remove: true);
        var view = Open(row);
        _api.Setup(x => x.RemoveCollectionAsync(It.IsAny<RemoveMobileCollectionRequest>())).Callback<RemoveMobileCollectionRequest>(r => sent = r)
            .ReturnsAsync((RemoveMobileCollectionRequest r) => Result<MobileCollectionCorrectionResult>.Success(new(Guid.NewGuid(), r.CollectionId, "SRC-2026-000021", null)));
        view.Find(".rc-row").Click();
        view.FindAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Remove").Click();

        Assert.Contains("Remove this collection?", view.Find("[role=dialog]").TextContent);
        Assert.Equal(["Entered by mistake", "Wrong payer or source", "Wrong amount", "Duplicate", "Other"], view.FindAll(".cr-option").Select(o => o.TextContent.Trim()).ToArray());
        Assert.True(view.Find(".sheet-actions .rc-danger").HasAttribute("disabled"));                       // nothing chosen yet
        view.FindAll(".cr-option").Single(o => o.TextContent.Contains("Other")).Click();
        Assert.True(view.Find(".sheet-actions .rc-danger").HasAttribute("disabled"));                       // Other needs a note
        view.Find(".cr input").Input("Taken twice");
        Assert.False(view.Find(".sheet-actions .rc-danger").HasAttribute("disabled"));

        view.Find(".sheet-actions .rc-danger").Click();

        view.WaitForAssertion(() => Assert.Contains("is reversed and stays on record", view.Find("[role=dialog]").TextContent));
        Assert.Equal(row.Collection.CollectionId, sent!.CollectionId);
        Assert.Equal((MobileCorrectionReason.Other, "Taken twice"), (sent.Reason.Code, sent.Reason.Note));
        Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
        _api.Verify(x => x.GetRecentCollectionsAsync(), Times.Exactly(1));
        view.Find(".sheet-actions .btn-primary").Click();                                                    // Done: the list reloads from the server
        view.WaitForAssertion(() => _api.Verify(x => x.GetRecentCollectionsAsync(), Times.Exactly(2)));
    }

    [Fact]
    public void ARemovalTheServerRefuses_IsExplainedFromItsCode_AndNothingIsClaimed()
    {
        var view = Open(Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", remove: true));
        _api.Setup(x => x.RemoveCollectionAsync(It.IsAny<RemoveMobileCollectionRequest>())).ReturnsAsync(Result<MobileCollectionCorrectionResult>.Failure("Remitted", EEMOCantilanSDS.Application.Common.ResultStatus.Conflict));
        view.Find(".rc-row").Click();
        view.FindAll(".sheet-actions button").Single(b => b.TextContent.Trim() == "Remove").Click();
        view.FindAll(".cr-option").First().Click();

        view.Find(".sheet-actions .rc-danger").Click();

        view.WaitForAssertion(() => Assert.Contains("already remitted", view.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("is reversed", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void WithoutAConnection_EditAndRemoveAreOffButNeverHidden_AndNothingIsSent()
    {
        _connection.Online = false;
        var view = Open(Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", edit: true, remove: true));
        view.Find(".rc-row").Click();

        var buttons = view.FindAll(".sheet-actions button").Where(b => b.TextContent.Trim() is "Edit" or "Remove").ToList();

        Assert.All(buttons, b => Assert.True(b.HasAttribute("disabled")));
        Assert.Contains("Connect to correct a collection", view.Find("[role=dialog]").TextContent);
    }

    [Fact]
    public void ACorrectedCollectionShowsItsNetAndSaysSo()
    {
        var view = Open(Recent("SRC-2026-000021", 75m, "Fish / Meat Vendor Fee", net: 0m, blockCode: "AlreadyCorrected", block: "Already corrected"));

        Assert.Contains("Removed", view.Find(".rc-row").TextContent);
        view.Find(".rc-row").Click();
        Assert.Contains("After correction", view.Find("[role=dialog]").TextContent);
    }
}
