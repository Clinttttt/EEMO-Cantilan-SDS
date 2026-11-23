using Bunit;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using PayBillModal = EEMOCantilanSDS.Client.Components.Pages.Shared.Actions.PayBillModal;

/// <summary>
/// Annual (panel: monthly and annual payment options): every unpaid month of one year on one OR, recorded oldest first through the same
/// per-month payment the other scopes use, so the server's rules (oldest month first, no overpaying) still decide each month.
/// </summary>
public class PayBillModalAnnualTests : TestContext
{
    private static readonly Guid StallId = Guid.NewGuid();
    private readonly List<RecordPaymentCommand> _recorded = [];

    private static PaymentHistoryDto Owed(string period, decimal paid = 0m) =>
        new(period, paid > 0m ? PaymentStatus.Partial : PaymentStatus.Unpaid, 4_800m, paid, 4_800m - paid, null, null);

    private IRenderedComponent<PayBillModal> RenderModal()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        List<PaymentHistoryDto> months = [Owed("2025-03", 2_400m), Owed("2025-09"), Owed("2025-12"), Owed("2026-01"), Owed("2026-02")];
        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(p => p.GetOutstandingMonthsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success(months));
        payments.Setup(p => p.RecordPaymentAsync(It.IsAny<RecordPaymentCommand>()))
            .ReturnsAsync((RecordPaymentCommand c) => { _recorded.Add(c); return Result<bool>.Success(true); });

        Services.AddSingleton(payments.Object);
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<PayBillModal>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.StallId, StallId)
            .Add(c => c.Facility, FacilityCode.TCC)
            .Add(c => c.StallNo, "D-08")
            .Add(c => c.Occupant, "Felix Santos"));
    }

    [Fact]
    public void Annual_PaysEveryUnpaidMonthOfTheChosenYear_OnOneOr_OldestFirst()
    {
        var cut = RenderModal();
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".pb-table tbody tr").Count));

        cut.FindAll(".pb-seg-btn").Single(b => b.TextContent.Trim() == "Annual").Click();

        // It starts on the oldest year: 2,400 still owed on March plus September and December in full.
        Assert.Contains("₱12,000.00", cut.Find(".pb-txn-amt").TextContent);
        Assert.Contains("3 months of 2025 · one OR", cut.Find(".pb-txnbar").TextContent);
        Assert.Equal(3, cut.FindAll(".pb-table tbody tr.on").Count);

        cut.Find("#pb-or").Input("7654321");
        cut.Find(".btn-primary").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, _recorded.Count));
        Assert.Equal(new[] { (2025, 3), (2025, 9), (2025, 12) }, _recorded.Select(c => (c.Year, c.Month)).ToArray());
        Assert.All(_recorded, c => Assert.Equal(("7654321", PaymentStatus.Paid), (c.ORNumber, c.Status)));
    }

    [Fact]
    public void Annual_ARowOfAnotherYear_PicksThatYear()
    {
        var cut = RenderModal();
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".pb-table tbody tr").Count));
        cut.FindAll(".pb-seg-btn").Single(b => b.TextContent.Trim() == "Annual").Click();

        cut.FindAll(".pb-table tbody tr")[3].Click();   // January 2026

        Assert.Contains("₱9,600.00", cut.Find(".pb-txn-amt").TextContent);
        Assert.Contains("2 months of 2026", cut.Find(".pb-txnbar").TextContent);
    }
}
