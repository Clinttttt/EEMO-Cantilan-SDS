using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Abstractions;
using EEMOCantilanSDS.Mobile.Components.Pages.Menus;
using EEMOCantilanSDS.Mobile.Models;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.Mobile.ComponentTests;

public sealed class NpmReceiptAmountTests : TestContext
{
    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    public void Receipt_displays_server_recorded_cash_instead_of_approved_daily_rate(int received)
    {
        var cut = CreateReceipt(received);
        var total = cut.Find(".receipt-grand").TextContent;
        Assert.Contains("Rent received today", total);
        Assert.Contains(received.ToString("N2"), total);
        Assert.DoesNotContain("Daily Fee", total);
        Assert.DoesNotContain("30.00", total);
    }

    [Fact]
    public void Without_verified_cash_amount_receipt_labels_daily_rate_as_a_rate()
    {
        var cut = CreateReceipt(null);
        var total = cut.Find(".receipt-grand").TextContent;
        Assert.Contains("Approved daily rate", total);
        Assert.Contains("30.00", total);
        Assert.DoesNotContain("Rent received today", total);
    }

    private IRenderedComponent<Market> CreateReceipt(decimal? received)
    {
        var date = new DateOnly(2026, 10, 28);
        var stall = new MobileNpmStallCollectionDto(
            Guid.NewGuid(), "17", "Riza Bucalon", "Riza Bucalon", MarketSection.FishSection,
            "Fish Area", StallStatus.Active, 30m, true, true, null, null, 1, 0, 1, 30m,
            MonthlyRentCollectedToday: 30m, RentReceivedOnBusinessDate: received);
        var api = new Mock<IMobileApiClient>();
        api.Setup(x => x.GetNpmCollectionAsync(2026, 10))
            .ReturnsAsync(Result<MobileNpmCollectionDto>.Success(
                new(2026, 10, date, 1, 1, 0, received ?? 30m, 0m, 1, 0, [stall])));
        Services.AddSingleton(api.Object);
        var store = new Mock<IPendingOperationStore>();
        store.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<PendingOperation>());
        Services.AddSingleton(new MobileSyncService(store.Object, api.Object,
            Mock.Of<IConnectivityMonitor>(), Mock.Of<ICurrentCollectorProvider>()));
        Services.AddSingleton(new MobileSessionService
        {
            Menu = new(Guid.NewGuid(), "Bobby Lugo", "test", date,
                [new(FacilityCode.NPM, "New Public Market", "Daily", true, true)])
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<Market>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tx-card")));
        cut.Find(".tx-card").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".receipt-grand")));
        return cut;
    }
}
