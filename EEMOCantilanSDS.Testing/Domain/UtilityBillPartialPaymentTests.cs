using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A utility bill records the TOTAL paid on each utility, not the last instalment — every screen that settles one
/// has to hand it the cumulative figure.
///
/// This is written down because the collector's app did not. It showed the balance still owed, took what the payor
/// handed over, and sent that instalment as the amount paid: a ₱100 collection against a ₱300 balance overwrote the
/// ₱200 already credited, so the money was taken and the payor's balance went UP to ₱400.
/// </summary>
public class UtilityBillPartialPaymentTests
{
    private static UtilityBill WaterBill(decimal cubicMetres, decimal ratePerCubicMetre) =>
        UtilityBill.Create(Guid.NewGuid(), 2026, 8,
            elecPreviousReading: 0m, elecCurrentReading: 0m, elecRatePerKwh: 0m,
            waterPreviousReading: 0m, waterCurrentReading: cubicMetres, waterRatePerCubicMeter: ratePerCubicMetre);

    private static void PayWater(UtilityBill bill, decimal totalPaid) =>
        bill.RecordPayment(
            elecOrNumber: null, waterOrNumber: "OR-1", collectorId: null,
            elecStatus: PaymentStatus.Unpaid, elecPartialAmount: null,
            waterStatus: PaymentStatus.Partial, waterPartialAmount: totalPaid);

    [Fact]
    public void ThePartialAmount_IsTheTotalPaid_NotTheLastInstalment()
    {
        var bill = WaterBill(cubicMetres: 500m, ratePerCubicMetre: 1m);   // ₱500 owed

        PayWater(bill, 200m);
        Assert.Equal(200m, bill.WaterAmountPaid);
        Assert.Equal(300m, bill.WaterBalanceDue);

        // A second collection of ₱100 is recorded as ₱300 in total — the figure every caller must send.
        PayWater(bill, 300m);

        Assert.Equal(300m, bill.WaterAmountPaid);
        Assert.Equal(200m, bill.WaterBalanceDue);
    }

    [Fact]
    public void SendingTheInstalmentAlone_WouldRaiseTheBalance()
    {
        // The defect, stated so it cannot come back: this is what the field app used to send.
        var bill = WaterBill(cubicMetres: 500m, ratePerCubicMetre: 1m);
        PayWater(bill, 200m);

        PayWater(bill, 100m);   // the instalment, not the total

        Assert.Equal(100m, bill.WaterAmountPaid);
        Assert.Equal(400m, bill.WaterBalanceDue);   // ₱100 collected, ₱100 MORE owed than before
    }

    [Fact]
    public void PayingTheWholeBalance_SettlesTheUtility()
    {
        var bill = WaterBill(cubicMetres: 500m, ratePerCubicMetre: 1m);
        PayWater(bill, 200m);

        PayWater(bill, 500m);   // the rest of it, as a total

        Assert.Equal(PaymentStatus.Paid, bill.WaterStatus);
        Assert.Equal(0m, bill.WaterBalanceDue);
        Assert.Equal(0m, bill.WaterPartialAmount);   // promoted to Paid, so no partial is left behind
    }
}
