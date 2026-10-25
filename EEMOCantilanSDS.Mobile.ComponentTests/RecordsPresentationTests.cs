using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;
public sealed class RecordsPresentationTests : TestContext
{
    [Fact]
    public void Multi_item_checkout_shows_one_record_with_each_child_SRC_and_instrument_in_the_sheet()
    {
        var today = PhilippineTime.Today;
        var collector = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var rows = new[]
        {
            new CollectionRegisterRowDto(ids[0], today, "SRC-2026-000021", "OR-011", RevenueInstrumentType.OfficialReceipt, "Sample Holder",
                collector, "Pat", Guid.NewGuid(), "Fish / Meat Vendor Fee", "Vendor", 56m, 0m, "Posted"),
            new CollectionRegisterRowDto(ids[1], today, "SRC-2026-000022", "OR-012", RevenueInstrumentType.OfficialReceipt, "Sample Holder",
                collector, "Pat", Guid.NewGuid(), "Weight & Measure", "Vendor", 132m, 0m, "Posted"),
            new CollectionRegisterRowDto(ids[2], today, "SRC-2026-000023", "CT-013", RevenueInstrumentType.CashTicket, "Sample Holder",
                collector, "Pat", Guid.NewGuid(), "Water", "Stall", 284m, 0m, "Posted")
        };
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetMyCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<CollectionsRegisterDto>.Success(new(today, today, rows, [],
                472m, 0m, 472m, false,
                [new(global::System.Guid.NewGuid(), ids)])));
        api.Setup(x => x.GetRecordsAsync(null, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success([]));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>();
        owner.SetupGet(x => x.CollectorKey).Returns("fixture:collector");
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSessionService());
        Services.AddSingleton(Mock.Of<IConnectivityMonitor>());
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
        var view = RenderComponent<EEMOCantilanSDS.Mobile.Components.Pages.Menus.Record>();
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".collection-record-row")));
        Assert.Contains("472.00", view.Find(".collection-record-row").TextContent);
        view.Find(".collection-record-row").Click();
        var sheet = view.Find("[aria-label='Session collection details']");
        Assert.Contains("472.00", sheet.QuerySelector(".session-total")!.TextContent);
        Assert.Equal(3, sheet.QuerySelectorAll(".session-item").Length);
        Assert.Equal(3, sheet.QuerySelectorAll(".session-identity").Length);
        Assert.Equal("Close", sheet.QuerySelector(".sheet-actions .session-close")!.TextContent.Trim());
        Assert.Contains("SRC-2026-000021", sheet.TextContent);
        Assert.Contains("SRC-2026-000022", sheet.TextContent);
        Assert.Contains("SRC-2026-000023", sheet.TextContent);
        Assert.Contains("OR-011", sheet.TextContent);
        Assert.Contains("CT-013", sheet.TextContent);
        view.Find(".search-wrap input").Input("SRC-2026-000022");
        Assert.Single(view.FindAll(".collection-record-row"));
        Assert.Contains("132.00", view.Find(".collection-record-row").TextContent);
        Assert.Contains("SRC-2026-000022", view.Find(".collection-record-row").TextContent);
        Assert.DoesNotContain("OR-012", view.Find(".collection-record-row").TextContent);
    }

    [Fact]
    public void Checkout_children_recorded_on_different_Philippine_days_keep_separate_day_cards()
    {
        var day = PhilippineTime.Today;
        var collector = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstUtc = DateTime.SpecifyKind(day.AddDays(-1).ToDateTime(new TimeOnly(23, 58)).AddHours(-8), DateTimeKind.Utc);
        var secondUtc = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(0, 2)).AddHours(-8), DateTimeKind.Utc);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetMyCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<CollectionsRegisterDto>.Success(new(day, day,
                [
                    new CollectionRegisterRowDto(firstId, day, "SRC-2026-000101", "CT-A", RevenueInstrumentType.CashTicket,
                        "Payor", collector, "Pat", Guid.NewGuid(), "Service A", "A", 30m, 0m, "Posted", RecordedAtUtc: firstUtc),
                    new CollectionRegisterRowDto(secondId, day, "SRC-2026-000102", "OR-B", RevenueInstrumentType.OfficialReceipt,
                        "Payor", collector, "Pat", Guid.NewGuid(), "Service B", "B", 70m, 0m, "Posted", RecordedAtUtc: secondUtc)
                ], [], 100m, 0m, 100m, false, [new(Guid.NewGuid(), [firstId, secondId])] )));
        api.Setup(x => x.GetRecordsAsync(null, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success([]));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>();
        owner.SetupGet(x => x.CollectorKey).Returns("fixture:collector");
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSessionService());
        Services.AddSingleton(Mock.Of<IConnectivityMonitor>());
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
        var view = RenderComponent<EEMOCantilanSDS.Mobile.Components.Pages.Menus.Record>();
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".collection-record-row").Count));
        Assert.Contains("SRC-2026-000101", view.Markup);
        Assert.Contains("SRC-2026-000102", view.Markup);
        Assert.DoesNotContain("2 collections", view.Markup);
    }

    [Fact]
    public void Page_keeps_canonical_and_legacy_identity_and_searches_both_without_literal_parameter_text()
    {
        var today = PhilippineTime.Today;
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetMyCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<CollectionsRegisterDto>.Success(new(today, today,
            [new(Guid.NewGuid(), today, "SRC-2026-000009", null, RevenueInstrumentType.OfficialReceipt, "Tricycad payer", null, "Collector", Guid.NewGuid(), "TRICYCAD", "Direct", 33m, 0m, "Posted", RecordedAtUtc: DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(10, 0)).AddHours(-8), DateTimeKind.Utc))], [], 33m, 0m, 33m, false)));
        api.Setup(x => x.GetRecordsAsync(null, It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success([
            new("HIST-123", "Ana Reyes", FacilityCode.NPM, "NPM", "F2", "Daily Fee", 900m, 870m, true, today.ToDateTime(new TimeOnly(9, 15)))]));
        var store = new Mock<IPendingOperationStore>(); store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("fixture:collector");
        Services.AddSingleton(api.Object); Services.AddSingleton(new MobileSessionService()); Services.AddSingleton(Mock.Of<IConnectivityMonitor>());
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
        var view = RenderComponent<EEMOCantilanSDS.Mobile.Components.Pages.Menus.Record>();
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".collection-record-row").Count));
        // The 10 AM canonical collection belongs above the 9:15 AM legacy market record.
        Assert.Contains("Tricycad payer", view.FindAll(".collection-record-row")[0].TextContent);
        Assert.Contains("Ana Reyes", view.FindAll(".collection-record-row")[1].TextContent);
        Assert.Contains("Tricycad payer", view.Markup); Assert.Contains("Ana Reyes", view.Markup);
        var legacy = view.FindAll("button.collection-record-row").Single(x => x.TextContent.Contains("Ana Reyes"));
        Assert.Contains("870.00", legacy.TextContent); Assert.Contains("900.00 assessed", legacy.TextContent);
        Assert.DoesNotContain("HIST-123", legacy.TextContent);
        Assert.DoesNotContain("No linked receipt", legacy.TextContent);
        Assert.DoesNotContain("Paid", legacy.TextContent);
        Assert.Contains("Daily Fee", legacy.TextContent);
        view.Find(".search-wrap input").Input("SRC-2026");
        Assert.Single(view.FindAll(".collection-record-row"));
        view.Find("button.collection-record-row").Click();
        Assert.Contains("SRC-2026-000009", view.Markup);
        view.Find(".search-wrap input").Input("Ana");
        Assert.Single(view.FindAll(".collection-record-row")); Assert.Contains("Ana Reyes", view.Markup);
        Assert.DoesNotContain("SRC-2026-000009", view.Markup);
    }
}
