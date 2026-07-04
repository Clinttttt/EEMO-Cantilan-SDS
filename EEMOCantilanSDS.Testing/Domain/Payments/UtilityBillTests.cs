using System;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Xunit;

namespace EEMOCantilanSDS.Testing;

/// <summary>Domain rules for the meter-based NPM utility bill (consumption × rate; independent elec/water payment).</summary>
public class UtilityBillTests
{
    private static UtilityBill Bill(decimal ePrev, decimal eCur, decimal eRate, decimal wPrev, decimal wCur, decimal wRate)
        => UtilityBill.Create(Guid.NewGuid(), 2026, 7, ePrev, eCur, eRate, wPrev, wCur, wRate, "admin");

    [Fact]
    public void Charges_AreConsumptionTimesRate()
    {
        var b = Bill(1200, 1248, 8m, 320, 328, 12m);
        Assert.Equal(48m, b.ElecConsumption);
        Assert.Equal(8m, b.WaterConsumption);
        Assert.Equal(384m, b.ElecCharge);
        Assert.Equal(96m, b.WaterCharge);
        Assert.Equal(480m, b.TotalCharge);
    }

    [Fact]
    public void Consumption_NeverNegative_WhenReadingLowerThanPrevious()
    {
        var b = Bill(1000, 990, 8m, 100, 90, 12m);
        Assert.Equal(0m, b.ElecConsumption);
        Assert.Equal(0m, b.WaterConsumption);
        Assert.Equal(0m, b.TotalCharge);
    }

    [Fact]
    public void ElectricityPaid_WaterUnpaid_IsOverallPartial()
    {
        var b = Bill(0, 10, 10m, 0, 5, 20m); // elec ₱100, water ₱100
        b.RecordPayment("OR-1", null, null, PaymentStatus.Paid, null, PaymentStatus.Unpaid, null, updatedBy: "admin");

        Assert.Equal(PaymentStatus.Paid, b.ElecStatus);
        Assert.Equal(PaymentStatus.Unpaid, b.WaterStatus);
        Assert.Equal(PaymentStatus.Partial, b.Status);          // overall
        Assert.Equal(100m, b.AmountPaid);
        Assert.Equal(100m, b.BalanceDue);                       // water still owed
    }

    [Fact]
    public void PartialElectricity_AutoUpgradesToPaid_WhenMeetingItsCharge()
    {
        var b = Bill(0, 10, 10m, 0, 0, 0m); // elec ₱100, water ₱0
        b.RecordPayment("OR-2", "OR-2", null, PaymentStatus.Partial, 100m, PaymentStatus.Paid, null, updatedBy: "admin");

        Assert.Equal(PaymentStatus.Paid, b.ElecStatus);
        Assert.Equal(0m, b.ElecPartialAmount);
        Assert.Equal(PaymentStatus.Paid, b.Status);
        Assert.Equal(0m, b.BalanceDue);
    }

    [Fact]
    public void PartialElectricity_KeepsBalance_AndAttributesCollector()
    {
        var b = Bill(0, 10, 10m, 0, 0, 0m); // elec ₱100
        var collector = Guid.NewGuid();
        b.RecordPayment("OR-3", null, collector, PaymentStatus.Partial, 40m, PaymentStatus.Unpaid, null, updatedBy: "msantos");

        Assert.Equal(PaymentStatus.Partial, b.ElecStatus);
        Assert.Equal(40m, b.ElecPartialAmount);
        Assert.Equal(60m, b.ElecBalanceDue);
        Assert.Equal(collector, b.CollectorId);
        Assert.Equal("OR-3", b.ElecORNumber);
    }

    [Fact]
    public void BothUnpaid_ClearsOrCollectorAndPaidAt()
    {
        var b = Bill(0, 10, 10m, 0, 5, 20m);
        b.RecordPayment("OR-4", "OR-4", Guid.NewGuid(), PaymentStatus.Paid, null, PaymentStatus.Paid, null, updatedBy: "admin");
        b.RecordPayment(null, null, null, PaymentStatus.Unpaid, null, PaymentStatus.Unpaid, null, updatedBy: "admin");

        Assert.Equal(PaymentStatus.Unpaid, b.Status);
        Assert.Null(b.ElecORNumber);
        Assert.Null(b.WaterORNumber);
        Assert.Null(b.CollectorId);
        Assert.Null(b.PaidAt);
    }
}
