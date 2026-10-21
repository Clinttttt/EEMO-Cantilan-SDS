using System.Reflection;
using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmOutstandingPagedUiTests : TestContext
{
    [Fact]
    public async Task Outstanding_screen_renders_all_verified_pages_without_using_unbounded_endpoint()
    {
        var today = new DateOnly(2026, 10, 20);
        var first = new NpmOutstandingRentAccount(Guid.NewGuid(), Guid.NewGuid(), "1", "Ana Reyes",
            "Vegetable Area", 2430m);
        var second = new NpmOutstandingRentAccount(Guid.NewGuid(), Guid.NewGuid(), "23", "Vincent Doloriel",
            "Vegetable Area", 3540m);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmCollectionAsync(2026, 10)).ReturnsAsync(Result<MobileNpmCollectionDto>.Success(
            new(2026, 10, today, 0, 0, 0, 0m, 0m, 0, 0, [])));
        api.Setup(x => x.GetNpmOutstandingAccountsPageAsync(0, 1)).ReturnsAsync(
            Result<NpmOutstandingRentAccountsPage>.Success(new([first], 1)));
        api.Setup(x => x.GetNpmOutstandingAccountsPageAsync(1, 1)).ReturnsAsync(
            Result<NpmOutstandingRentAccountsPage>.Success(new([second], null)));
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(api.Object);
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object,
            Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));
        Services.AddSingleton(new MobileSessionService { Menu = new(Guid.NewGuid(), "Collector", "test", today,
            [new(FacilityCode.NPM, "New Public Market", "Daily", true, true)]) });
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<Market>();
        await cut.InvokeAsync(async () => await (Task)typeof(Market)
            .GetMethod("OpenArrears", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cut.Instance, [])!);
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Markup));
        Assert.Contains("Vincent Doloriel", cut.Markup);
        api.Verify(x => x.GetNpmOutstandingAccountsPageAsync(0, 1), Times.AtLeastOnce);
        api.Verify(x => x.GetNpmOutstandingAccountsPageAsync(1, 1), Times.AtLeastOnce);
        api.Verify(x => x.GetNpmOutstandingAccountsAsync(), Times.Never);
    }
}
