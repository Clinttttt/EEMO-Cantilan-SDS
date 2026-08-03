using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A real defect the office hit: the office recorded a water bill for a stall, and the collector's app said
/// "no electricity or water bill recorded for this stall" — the field app could only ever see the bills of the
/// month its sheet was on, so a bill left unpaid became uncollectible in the field the moment the month turned
/// over. What the collector must still answer for is stated here, once.
/// </summary>
public class UtilityBillMonthAndStillOwedTests
{
    private static UtilityBill Bill(int year, int month, decimal waterCurrent = 0m, decimal waterRate = 0m,
                                    decimal elecCurrent = 0m, decimal elecRate = 0m) =>
        UtilityBill.Create(Guid.NewGuid(), year, month,
            elecPreviousReading: 0m, elecCurrentReading: elecCurrent, elecRatePerKwh: elecRate,
            waterPreviousReading: 0m, waterCurrentReading: waterCurrent, waterRatePerCubicMeter: waterRate);

    private static void SettleWater(UtilityBill bill, PaymentStatus status, decimal? partial = null) =>
        bill.RecordPayment(
            elecOrNumber: null, waterOrNumber: "OR-1", collectorId: null,
            elecStatus: PaymentStatus.Unpaid, elecPartialAmount: null,
            waterStatus: status, waterPartialAmount: partial);

    [Fact]
    public void TheMonthsOwnBills_AreAlwaysIncluded_EvenWhenSettled()
    {
        var bill = Bill(2026, 8, waterCurrent: 56m, waterRate: 1m);
        SettleWater(bill, PaymentStatus.Paid);

        var kept = UtilityBill.MonthAndStillOwed(new[] { bill }, 2026, 8);

        Assert.Single(kept);   // the collector can still see what was already settled this month
    }

    [Fact]
    public void AnUnpaidBillFromAnEarlierMonth_IsStillCollectible()
    {
        var july = Bill(2026, 7, waterCurrent: 56m, waterRate: 1m);   // ₱56 owed, never paid
        var august = Bill(2026, 8, waterCurrent: 60m, waterRate: 1m);

        var kept = UtilityBill.MonthAndStillOwed(new[] { august, july }, 2026, 8);

        Assert.Equal(2, kept.Count);
        Assert.Equal(7, kept[0].BillingMonth);   // oldest owed first — that is the one to settle
        Assert.Equal(8, kept[1].BillingMonth);
    }

    [Fact]
    public void AnEarlierBillThatWasPaid_IsNotOfferedAgain()
    {
        var july = Bill(2026, 7, waterCurrent: 56m, waterRate: 1m);
        SettleWater(july, PaymentStatus.Paid);
        var august = Bill(2026, 8, waterCurrent: 60m, waterRate: 1m);

        var kept = UtilityBill.MonthAndStillOwed(new[] { july, august }, 2026, 8);

        Assert.Single(kept);
        Assert.Equal(8, kept[0].BillingMonth);
    }

    [Fact]
    public void AnEarlierBillPartlyPaid_IsStillOwed()
    {
        var july = Bill(2026, 7, waterCurrent: 56m, waterRate: 1m);
        SettleWater(july, PaymentStatus.Partial, partial: 20m);

        var kept = UtilityBill.MonthAndStillOwed(new[] { july }, 2026, 8);

        Assert.Single(kept);
        Assert.Equal(36m, kept[0].BalanceDue);
    }

    [Fact]
    public void ALaterMonthsBill_IsNotBroughtForward()
    {
        var september = Bill(2026, 9, waterCurrent: 60m, waterRate: 1m);

        var kept = UtilityBill.MonthAndStillOwed(new[] { september }, 2026, 8);

        Assert.Empty(kept);
    }

    [Fact]
    public void AWaterOnlyBill_CarriesItsWaterChargeAndNoElectricity()
    {
        // The reported case: 56 cu.m at ₱1.00, no electricity reading at all.
        var bill = Bill(2026, 8, waterCurrent: 56m, waterRate: 1m);

        var kept = UtilityBill.MonthAndStillOwed(new[] { bill }, 2026, 8);

        Assert.Single(kept);
        Assert.Equal(56m, kept[0].WaterCharge);
        Assert.Equal(0m, kept[0].ElecCharge);
        Assert.Equal(56m, kept[0].BalanceDue);
    }

    /// <summary>
    /// Over-collection on one utility must not mask the other. The bill's net balance can go negative — a
    /// zero-charge electricity side marked Partial with an amount is accepted (Normalize only promotes Partial
    /// to Paid when there is a charge) — and the water still owed would then have vanished from the field.
    /// </summary>
    [Fact]
    public void OneUtilityOverCollected_DoesNotHideTheOtherStillOwed()
    {
        var july = Bill(2026, 7, waterCurrent: 56m, waterRate: 1m);   // ₱56 of water owed
        july.RecordPayment(
            elecOrNumber: "OR-E", waterOrNumber: null, collectorId: null,
            elecStatus: PaymentStatus.Partial, elecPartialAmount: 500m,   // against a ₱0 electricity charge
            waterStatus: PaymentStatus.Unpaid, waterPartialAmount: null);

        Assert.True(july.BalanceDue < 0m);          // the net figure says nothing is owed…
        Assert.Equal(56m, july.WaterBalanceDue);    // …while water plainly is

        var kept = UtilityBill.MonthAndStillOwed(new[] { july }, 2026, 8);

        Assert.Single(kept);
    }
}
