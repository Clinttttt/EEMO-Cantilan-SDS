using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public class PaymentRecordTests
{
    private static PaymentRecord NewPayment(decimal baseRental = 900m)
        => PaymentRecord.Create(Guid.NewGuid(), 2026, 1, baseRental);

    // Regression: collector/report aggregations must count a Partial payment at PartialAmount,
    // never the full bill.
    [Fact]
    public void Partial_RecognizesPartialAmount_NotFullBill()
    {
        var payment = NewPayment(900m);
        payment.UpdateStatus(PaymentStatus.Partial, partialAmount: 300m);

        Assert.Equal(PaymentStatus.Partial, payment.Status);
        Assert.Equal(900m, payment.TotalBill);
        Assert.Equal(300m, payment.AmountPaid);
        Assert.Equal(600m, payment.BalanceDue);
    }

    [Fact]
    public void Paid_RecognizesFullBill()
    {
        var payment = NewPayment(900m);
        payment.UpdateStatus(PaymentStatus.Paid);

        Assert.Equal(900m, payment.AmountPaid);
        Assert.Equal(0m, payment.BalanceDue);
    }

    [Fact]
    public void Unpaid_RecognizesNothing()
    {
        var payment = NewPayment(900m);

        Assert.Equal(PaymentStatus.Unpaid, payment.Status);
        Assert.Equal(0m, payment.AmountPaid);
        Assert.Equal(900m, payment.BalanceDue);
    }

    [Fact]
    public void Partial_AtOrAboveTotal_AutoUpgradesToPaid()
    {
        var payment = NewPayment(900m);
        payment.UpdateStatus(PaymentStatus.Partial, partialAmount: 900m);

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(900m, payment.AmountPaid);
        Assert.Equal(0m, payment.BalanceDue);
    }

    // Regression for P5-c: attaching an OR number must not wipe the fee breakdown or status.
    [Fact]
    public void SetOrNumber_PreservesFeeBreakdownAndStatus()
    {
        var payment = NewPayment(900m);
        payment.RecordPayment("OLD", Guid.NewGuid(), PaymentStatus.Paid,
            partialAmount: null, elecAmount: 100m, waterAmount: 50m, fishKilos: 10m);
        var billBefore = payment.TotalBill;

        payment.SetOrNumber("OR-2026-001", "admin");

        Assert.Equal("OR-2026-001", payment.ORNumber);
        Assert.Equal(100m, payment.ElecAmount);
        Assert.Equal(50m, payment.WaterAmount);
        Assert.Equal(10m, payment.FishKilos);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(billBefore, payment.TotalBill);
    }
}
