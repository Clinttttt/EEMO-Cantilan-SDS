using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

/// <summary>
/// The Records page lists the Collector's canonical collections. A mistake in a collection recorded today is corrected through the existing
/// recent-collections sheet (the server decides who may), offered only while the period includes today and never as an in-place edit.
/// </summary>
public sealed class OperationRecordsSectionTests : TestContext
{
    private readonly Mock<IMobileApiClient> _api = new();
    private static readonly DateOnly Today = PhilippineTime.Today;

    public OperationRecordsSectionTests()
    {
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(() => Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("tenant:collector");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, _api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
        Services.AddSingleton(Mock.Of<Microsoft.AspNetCore.Components.NavigationManager>());
        _api.Setup(x => x.GetMyCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<CollectionsRegisterDto>.Success(new(
            Today, Today, [new(Guid.NewGuid(), Today, "SRC-2026-000042", null, RevenueInstrumentType.OfficialReceipt, "Giorgia Macapagal", null, "Pat", Guid.NewGuid(),
                "Fish / Meat Vendor Fee", "Direct", 43m, 0m, "Posted")], [], 43m, 0m, 43m, false)));
        _api.Setup(x => x.GetRecentCollectionsAsync()).ReturnsAsync(Result<MobileRecentCollections>.Success(new(Today, [], false)));
    }

    [Fact]
    public void TodaysCollectionOffersACorrectionEntry_ThatOpensTheRecentCollectionsList()
    {
        var view = RenderComponent<OperationRecordsSection>(p => p.Add(x => x.From, Today.AddDays(-5)).Add(x => x.To, Today));
        view.WaitForAssertion(() => Assert.Contains("None recorded", view.Markup));
        view.Find(".collection-record-row").Click();
        Assert.Contains("SRC-2026-000042", view.Markup);
        var open = view.Find(".ors-correct");
        Assert.Equal("false", open.GetAttribute("aria-expanded"));
        Assert.Empty(view.FindAll(".rc"));

        open.Click();

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll(".rc")));
        Assert.Equal("true", view.Find(".ors-correct").GetAttribute("aria-expanded"));
        Assert.Contains("Hide today's collections", view.Find(".ors-correct").TextContent);
        _api.Verify(x => x.GetRecentCollectionsAsync(), Times.Once);     // the server's own list; nothing is corrected from this page
    }

    [Fact]
    public void APastPeriodOffersNoCorrection()
    {
        var view = RenderComponent<OperationRecordsSection>(p => p.Add(x => x.From, Today.AddDays(-40)).Add(x => x.To, Today.AddDays(-10)));
        view.WaitForAssertion(() => Assert.Contains("None recorded", view.Markup));
        Assert.Empty(view.FindAll(".ors-correct"));
        _api.Verify(x => x.GetRecentCollectionsAsync(), Times.Never);
    }
}
