using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
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
    public void Period_controls_opt_in_close_each_other_and_selection_reloads_the_quote()
    {
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmWholePaymentQuoteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((Guid stall, int year, int month) => Result<NpmWholePaymentQuoteDto>.Success(
                new(stall, year, month, new(2026, 10, 7), 30, 900m, 0m, "reviewed", RevenueInstrumentType.OfficialReceipt, 900m, 0m)));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        var connectivity = new Mock<IConnectivityMonitor>(); connectivity.SetupGet(x => x.IsOnline).Returns(true);
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, connectivity.Object, Mock.Of<ICurrentCollectorProvider>()));
        Services.AddSingleton(new MobileSessionService());
        var stallId = Guid.NewGuid();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/npm/whole-payment/{stallId}?Year=2026&Month=10");
        var view = RenderComponent<NpmWholePayment>(p => p.Add(x => x.StallId, stallId));
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".whole-review")));
        var controls = view.FindComponents<MobileChoice>();
        Assert.Equal(2, controls.Count); Assert.All(controls, x => Assert.True(x.Instance.Popover));
        controls[0].Find(".mc-trigger").Click(); Assert.Equal(12, controls[0].FindAll(".mc-option").Count);
        controls[1].Find(".mc-trigger").Click(); Assert.Empty(controls[0].FindAll(".mc-list"));
        Assert.Single(controls[1].FindAll(".mc-list"));
        controls[1].FindAll(".mc-option")[0].Click();
        view.WaitForAssertion(() => api.Verify(x => x.GetNpmWholePaymentQuoteAsync(stallId, 2024, 10), Times.Once));
        Assert.Empty(view.FindAll(".mc-list"));
        controls[0].Find(".mc-trigger").Click(); controls[0].FindAll(".mc-option")[0].Click();
        view.WaitForAssertion(() => api.Verify(x => x.GetNpmWholePaymentQuoteAsync(stallId, 2024, 1), Times.Once));
        Assert.Contains("January 2024", view.Find(".whole-review").TextContent);
    }
}
