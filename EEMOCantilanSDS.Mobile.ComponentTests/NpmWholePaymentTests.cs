using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Components.Shared;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.AspNetCore.Components;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmWholePaymentTests : TestContext
{
    [Fact]
    public void Outstanding_page_uses_one_amount_form_and_server_total_without_month_controls()
    {
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmOutstandingQuoteAsync(It.IsAny<Guid>(), null, null))
            .ReturnsAsync((Guid stall, Guid? contract, decimal? amount) => Result<NpmOutstandingRentQuote>.Success(
                new(stall, Guid.NewGuid(), "1", "Ana Reyes", new(2026, 10, 20), 2430, 2430, 0, "reviewed", [])));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var connectivity = new Mock<IConnectivityMonitor>(); connectivity.SetupGet(x => x.IsOnline).Returns(true);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, connectivity.Object, Mock.Of<ICurrentCollectorProvider>()));
        Services.AddSingleton(new MobileSessionService());
        var stallId = Guid.NewGuid();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/npm/whole-payment/{stallId}?Year=2026&Month=10");
        var view = RenderComponent<NpmWholePayment>(p => p.Add(x => x.StallId, stallId));
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".rent-amount-input")));
        Assert.Empty(view.FindComponents<MobileChoice>());
        Assert.Equal("2430.00", view.Find(".rent-amount-input").GetAttribute("value"));
        Assert.Contains("2,430.00", view.Markup);
        Assert.Single(view.FindAll(".rent-confirm"));
        Assert.Contains("Official Receipt No. (Optional)", view.Markup);
        Assert.Single(view.FindAll("input[autocomplete='off']"));
        Assert.False(view.Find(".rent-allocation").HasAttribute("open"));
        Assert.Single(view.FindAll("a[href='/records']"));
        api.Verify(x => x.GetNpmWholePaymentQuoteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Edited_amount_displays_server_allocation_and_preserves_daily_rate()
    {
        var api = new Mock<IMobileApiClient>(); var stall = Guid.NewGuid(); var contract = Guid.NewGuid();
        api.Setup(x => x.GetNpmOutstandingQuoteAsync(stall, null, null)).ReturnsAsync(Result<NpmOutstandingRentQuote>.Success(
            new(stall, contract, "1", "Ana Reyes", new(2026, 10, 20), 2430, 2430, 0, "full", [])));
        api.Setup(x => x.GetNpmOutstandingQuoteAsync(stall, contract, 1000)).ReturnsAsync(Result<NpmOutstandingRentQuote>.Success(
            new(stall, contract, "1", "Ana Reyes", new(2026, 10, 20), 2430, 1000, 1430, "partial",
                [new(new(2026, 6, 1), CollectionSourceKind.DailyCollection, 240, 240, 0), new(new(2026, 7, 1), CollectionSourceKind.DailyCollection, 780, 760, 20)])));
        var store = new Mock<IPendingOperationStore>(); store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var online = new Mock<IConnectivityMonitor>(); online.SetupGet(x => x.IsOnline).Returns(true);
        Services.AddSingleton(api.Object); Services.AddSingleton(new MobileSyncService(store.Object, api.Object, online.Object, Mock.Of<ICurrentCollectorProvider>()));
        var cut = RenderComponent<NpmRentAmountForm>(p => p.Add(x => x.StallId, stall).Add(x => x.ApprovedDailyRate, 30));
        cut.Find("input").Input("1000");
        cut.WaitForAssertion(() => Assert.Contains("1,430.00", cut.Markup));
        Assert.Contains("30.00", cut.Markup); Assert.Contains("June 2026", cut.Markup); Assert.Contains("760.00", cut.Markup);
        Assert.Single(cut.FindAll(".rent-confirm"));
        api.Verify(x => x.RecordNpmOutstandingRentAsync(It.IsAny<NpmOutstandingRentPayment>()), Times.Never);
    }

    [Fact]
    public void Ordinary_daily_installment_retains_its_supported_offline_capture_action()
    {
        var api = new Mock<IMobileApiClient>(); var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(api.Object); Services.AddSingleton(new MobileSyncService(store.Object, api.Object,
            Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));
        var confirmed = false;
        var cut = RenderComponent<NpmRentAmountForm>(p => p.Add(x => x.StallId, Guid.NewGuid()).Add(x => x.DailyAmount, 30)
            .Add(x => x.ApprovedDailyRate, 30).Add(x => x.OnDailyConfirm, () => confirmed = true));
        cut.Find("input[autocomplete='off']").Input("TEST-123");
        Assert.Equal("TEST-123", cut.Instance.OfficialReceiptNumber);
        cut.Find(".rent-confirm").Click(); Assert.True(confirmed);
        cut.Find("input").Input("50");
        Assert.True(cut.Find(".rent-confirm").HasAttribute("disabled"));
        Assert.Contains("Connect to review", cut.Markup);
    }
}
