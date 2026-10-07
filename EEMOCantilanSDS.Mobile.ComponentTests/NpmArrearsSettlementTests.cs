using System.Reflection;
using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmArrearsSettlementTests : TestContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Legacy_arrears_sheet_confirms_without_a_physical_receipt_serial(bool closedMonth)
    {
        var today = new DateOnly(2026, 10, 7);
        var stall = Guid.NewGuid();
        var month = new MobileNpmMonthArrearDto(2026, 6, 8, 240m);
        DateOnly[] days = [today.AddDays(-2), today.AddDays(-1)];
        var payor = new MobileNpmStallArrearsDto(stall, "1", "Ana Reyes", MarketSection.VegetableArea,
            "Vegetable area", 30m, [month], days, 60m);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmCollectionAsync(2026, 10)).ReturnsAsync(Result<MobileNpmCollectionDto>.Success(
            new(2026, 10, today, 0, 0, 0, 0m, 0m, 0, 0, [])));
        api.Setup(x => x.GetNpmArrearsAsync(2026, 10)).ReturnsAsync(Result<MobileNpmArrearsDto>.Success(new(2026, 10, today, 300m, [payor])));
        api.Setup(x => x.SettleNpmMonthAsync(It.IsAny<SettleMobileNpmMonthRequest>())).ReturnsAsync(Result<bool>.Success(true));
        api.Setup(x => x.SettleNpmDaysAsync(It.IsAny<SettleMobileNpmDaysRequest>())).ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(api.Object);
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object, Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));
        Services.AddSingleton(new MobileSessionService { Menu = new(Guid.NewGuid(), "Scratch collector", "test", today,
            [new(FacilityCode.NPM, "New Public Market", "Daily", true, true)]) });
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<Market>();
        // Open the actual legacy confirmation sheet with the server-owned arrears facts.
        await cut.InvokeAsync(() => typeof(Market).GetMethod(closedMonth ? "OpenMonthSettle" : "OpenDaysSettle",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cut.Instance, closedMonth ? [payor, month] : [payor]));
        cut.Render();
        var sheet = cut.FindAll(".sheet").Single(x => x.QuerySelector(".sheet-sub")?.TextContent.Contains("Ana Reyes") == true);
        Assert.Empty(sheet.QuerySelectorAll("input"));
        Assert.Contains("Ana Reyes", sheet.TextContent);
        Assert.Contains(closedMonth ? "June 2026" : "October 2026", sheet.TextContent);
        sheet.QuerySelectorAll("button").Single(x => x.TextContent.Trim() == "Confirm").Click();
        cut.WaitForAssertion(() =>
        {
            if (closedMonth)
                api.Verify(x => x.SettleNpmMonthAsync(It.Is<SettleMobileNpmMonthRequest>(r => r.StallId == stall && r.Year == 2026 && r.Month == 6 && r.ORNumber == null)), Times.Once);
            else
                api.Verify(x => x.SettleNpmDaysAsync(It.Is<SettleMobileNpmDaysRequest>(r => r.StallId == stall && r.ORNumber == null && r.Dates!.SequenceEqual(days))), Times.Once);
            Assert.DoesNotContain("Enter the OR number", cut.Markup);
        });
    }
}
