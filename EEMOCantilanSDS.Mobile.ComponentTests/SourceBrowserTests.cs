using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>
/// The registered-source browser on New Collection: the collector sees the sources assigned to them, by type and in bounded pages, each by its own
/// exact identity. It states no amount or balance, and offline it says so instead of showing stale rows.
/// </summary>
public sealed class SourceBrowserTests : TestContext
{
    private readonly Mock<IMobileApiClient> _api = new();
    private static readonly CollectionSourceIdentity Vendor = new(SourceIdentityKind.FishMeatVendorRegistration, Guid.NewGuid());
    private static readonly CollectionSourceIdentity Stall = new(SourceIdentityKind.Occupancy, Guid.NewGuid());
    private static readonly CollectionSourceIdentity Space = new(SourceIdentityKind.SpaceAccount, Guid.NewGuid());
    private static readonly IReadOnlyList<CollectionSourceCategoryDto> Categories =
        [new("fishmeat", "Fish / Meat"), new("npm", "NPM"), new("spaces", "Spaces")];

    private static CollectionSourceSearchResult Row(CollectionSourceIdentity id, string name, string context, string op = "OP") => new(id, name, context, op);

    private void Serve(string? category, IReadOnlyList<CollectionSourceSearchResult> items, int total, int skip = 0) =>
        _api.Setup(x => x.BrowseCollectionSourcesAsync(category, null, skip, 25))
            .ReturnsAsync(Result<CollectionSourcePage>.Success(new(items, total, Categories)));

    public SourceBrowserTests() => Services.AddSingleton(_api.Object);

    [Fact]
    public void ListsTheAssignedSourcesWithTheirOwnContext_AndNoAmountOrBalance()
    {
        Serve(null, [Row(Vendor, "Pantom Dant", "Meat · 2026"), Row(Stall, "Ana Reyes", "New Public Market · Stall 1"), Row(Space, "Juan Cruz", "Kanmanggay · Space 4")], 3);

        var cut = RenderComponent<SourceBrowser>(p => p.Add(x => x.OnSelect, EventCallback.Factory.Create<CollectionSourceSearchResult>(this, _ => { })));

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".sb-row").Count));
        Assert.Contains("New Public Market · Stall 1", cut.Markup);
        Assert.DoesNotContain("₱", cut.Markup);
        Assert.Equal(["All", "Fish / Meat", "NPM", "Spaces"], cut.FindAll(".sb-chip").Select(c => c.TextContent.Trim()).ToArray());
        Assert.Equal("3 of 3", cut.Find(".sb-count").TextContent.Trim());
    }

    [Fact]
    public void ChoosingATypeAsksForThatTypeOnly_AndSelectingARowHandsOverItsExactIdentity()
    {
        Serve(null, [Row(Vendor, "Pantom Dant", "Meat · 2026"), Row(Stall, "Ana Reyes", "New Public Market · Stall 1")], 2);
        Serve("npm", [Row(Stall, "Ana Reyes", "New Public Market · Stall 1")], 1);
        CollectionSourceSearchResult? chosen = null;
        var cut = RenderComponent<SourceBrowser>(p => p.Add(x => x.OnSelect, EventCallback.Factory.Create<CollectionSourceSearchResult>(this, s => chosen = s)));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".sb-row").Count));

        cut.FindAll(".sb-chip").Single(c => c.TextContent.Trim() == "NPM").Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".sb-row")));
        Assert.Equal("true", cut.FindAll(".sb-chip").Single(c => c.TextContent.Trim() == "NPM").GetAttribute("aria-pressed"));
        cut.Find(".sb-row").Click();
        Assert.Equal(Stall, chosen!.Identity);
        _api.Verify(x => x.BrowseCollectionSourcesAsync("npm", null, 0, 25), Times.Once);
    }

    [Fact]
    public void ShowMoreAppendsTheNextPage_WithoutRepeatingARow()
    {
        var first = Enumerable.Range(1, 25).Select(i => Row(new(SourceIdentityKind.Occupancy, new Guid(i, 0, 0, new byte[8])), $"Holder {i:00}", "NCC · Stall " + i)).ToArray();
        Serve(null, first, 27);
        Serve(null, [first[24], Row(Stall, "Holder 26", "NCC · Stall 26"), Row(Space, "Holder 27", "Kanmanggay · Space 1")], 27, skip: 25);
        var cut = RenderComponent<SourceBrowser>(p => p.Add(x => x.OnSelect, EventCallback.Factory.Create<CollectionSourceSearchResult>(this, _ => { })));
        cut.WaitForAssertion(() => Assert.Equal(25, cut.FindAll(".sb-row").Count));
        Assert.Equal("25 of 27", cut.Find(".sb-count").TextContent.Trim());

        cut.Find(".sb-more").Click();

        cut.WaitForAssertion(() => Assert.Equal(27, cut.FindAll(".sb-row").Count));   // 25 + 2 new; the repeated row is dropped
        Assert.Empty(cut.FindAll(".sb-more"));
    }

    [Fact]
    public void WhenTheReadFails_ItSaysToConnect_AndNeverShowsStaleRows()
    {
        _api.Setup(x => x.BrowseCollectionSourcesAsync(null, null, 0, 25)).ReturnsAsync(Result<CollectionSourcePage>.Failure("offline"));

        var cut = RenderComponent<SourceBrowser>(p => p.Add(x => x.OnSelect, EventCallback.Factory.Create<CollectionSourceSearchResult>(this, _ => { })));

        cut.WaitForAssertion(() => Assert.Contains("Connect to browse registered sources", cut.Markup));
        Assert.Empty(cut.FindAll(".sb-row"));
        Assert.NotNull(cut.Find(".sb-more"));
    }

    [Fact]
    public void AnEmptyAssignmentIsStatedPlainly()
    {
        _api.Setup(x => x.BrowseCollectionSourcesAsync(null, null, 0, 25)).ReturnsAsync(Result<CollectionSourcePage>.Success(new([], 0, [])));

        var cut = RenderComponent<SourceBrowser>(p => p.Add(x => x.OnSelect, EventCallback.Factory.Create<CollectionSourceSearchResult>(this, _ => { })));

        cut.WaitForAssertion(() => Assert.Contains("No registered sources are assigned to you", cut.Markup));
        Assert.Empty(cut.FindAll(".sb-chip"));
    }
}

/// <summary>On the New Collection landing the registered sources replace Recent Collections; corrections stay in Records.</summary>
public sealed class NewCollectionLandingTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private readonly Mock<IMobileApiClient> _api = new();
    private static readonly CollectionSourceIdentity Stall = new(SourceIdentityKind.Occupancy, Guid.NewGuid());

    private sealed class Connectivity : IConnectivityMonitor { public bool IsOnline => true; public event Action? ConnectivityRestored { add { } remove { } } }

    public NewCollectionLandingTests()
    {
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => []);
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("tenant:collector");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, new Connectivity(), owner.Object));
        _api.Setup(x => x.BrowseCollectionSourcesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<CollectionSourcePage>.Success(new([new(Stall, "Ana Reyes", "New Public Market · Stall 1", "FACILITY_NPM")], 1, [new("npm", "NPM")])));
        _api.Setup(x => x.GetSourceCollectionDiscoveryAsync(It.IsAny<CollectionSourceIdentity?>()))
            .ReturnsAsync(Result<CollectionSessionDiscovery>.Success(new(null, Today, [], null)));
    }

    [Fact]
    public void TheLandingShowsRegisteredSources_NotRecentCollections_AndKeepsDirectCollectionAndRecords()
    {
        var cut = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".sb-row")));
        Assert.Empty(cut.FindAll(".rc"));                                        // Recent Collections is no longer the default landing
        _api.Verify(x => x.GetRecentCollectionsAsync(), Times.Never);
        var actions = cut.Find(".landing-actions").TextContent;
        Assert.Contains("Direct collection", actions);
        Assert.Contains("Records", actions);
    }

    [Fact]
    public void ChoosingARegisteredSourceOpensThatExactSource()
    {
        var cut = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".sb-row")));

        cut.Find(".sb-row").Click();

        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Find(".payer-selected").TextContent));
        _api.Verify(x => x.GetSourceCollectionDiscoveryAsync(Stall), Times.AtLeastOnce);
        Assert.Empty(cut.FindAll(".sb-row"));                                    // the browser gives way to the selected-source workspace
    }

    [Fact]
    public void RecordsOpensTheDedicatedRecordsPageWhereCorrectionsLive()
    {
        var cut = RenderComponent<CollectionSessionEditor>(p => p.Add(x => x.BusinessDate, Today));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".sb-row")));

        cut.FindAll(".landing-actions button").Single(b => b.TextContent.Trim() == "Records").Click();

        Assert.EndsWith("/records", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
