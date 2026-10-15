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
    public void Page_keeps_canonical_and_legacy_identity_and_searches_both_without_literal_parameter_text()
    {
        var today = PhilippineTime.Today;
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetMyCollectionsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<CollectionsRegisterDto>.Success(new(today, today,
            [new(Guid.NewGuid(), today, "SRC-2026-000009", null, RevenueInstrumentType.OfficialReceipt, "Tricycad payer", null, "Collector", Guid.NewGuid(), "TRICYCAD", "Direct", 33m, 0m, "Posted")], [], 33m, 0m, 33m, false)));
        api.Setup(x => x.GetRecordsAsync(null, It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(Result<IReadOnlyList<MobileCollectorRecordDto>>.Success([
            new("HIST-123", "Ana Reyes", FacilityCode.NPM, "NPM", "F2", "Daily Fee", 900m, 870m, true, today.ToDateTime(new TimeOnly(9, 15)))]));
        var store = new Mock<IPendingOperationStore>(); store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var owner = new Mock<ICurrentCollectorProvider>(); owner.SetupGet(x => x.CollectorKey).Returns("fixture:collector");
        Services.AddSingleton(api.Object); Services.AddSingleton(new MobileSessionService()); Services.AddSingleton(Mock.Of<IConnectivityMonitor>());
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), owner.Object));
        var view = RenderComponent<EEMOCantilanSDS.Mobile.Components.Pages.Menus.Record>();
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".collection-record-row").Count));
        Assert.Contains("SRC-2026-000009", view.Markup); Assert.Contains("Ana Reyes", view.Markup);
        var legacy = view.Find("button.collection-record-row");
        Assert.Contains("870.00", legacy.TextContent); Assert.Contains("900.00 assessed", legacy.TextContent);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(legacy.TextContent, "HIST-123").Count);
        view.Find(".search-wrap input").Input("SRC-2026");
        Assert.Single(view.FindAll(".collection-record-row")); Assert.Contains("SRC-2026-000009", view.Markup);
        view.Find(".search-wrap input").Input("Ana");
        Assert.Single(view.FindAll(".collection-record-row")); Assert.Contains("Ana Reyes", view.Markup);
        Assert.DoesNotContain("SRC-2026-000009", view.Markup);
    }
}
