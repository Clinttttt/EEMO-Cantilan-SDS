using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
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

    [Fact]
    public void LegacySettlementMutationsAdvanceTheRentSourceVersion()
    {
        var payment = NewPayment(900m);
        var initial = payment.SettlementVersion;

        payment.UpdateStatus(PaymentStatus.Partial, 200m);

        Assert.Equal(initial + 1, payment.SettlementVersion);
        Assert.Equal(200m, payment.AmountPaid);
    }

    [Fact]
    public void CanonicalRentProjectionShowsNetCanonicalRentInLegacyReaders()
    {
        var tenant = Guid.NewGuid();
        var payment = NewPayment(900m);
        payment.UpdateStatus(PaymentStatus.Partial, 200m);
        payment.MarkSettlementPendingCutover();
        var at = DateTime.SpecifyKind(new DateTime(2026, 9, 26, 10, 0, 0), DateTimeKind.Utc);
        var cutover = CollectionSettlementCutover.Freeze(tenant, CollectionSourceKind.PaymentRecord,
            payment.Id, null, payment.SettlementVersion, at, 900m, 200m, 700m,
            "{\"reconciled\":true}", Guid.NewGuid(), at.AddMinutes(1));
        payment.ActivateCanonicalSettlement(cutover);

        payment.ApplyCanonicalRentProjection(500m, at.AddMinutes(2), "admin");

        Assert.Equal(PaymentStatus.Partial, payment.Status);
        Assert.Equal(500m, payment.PartialAmount);
        Assert.Equal(400m, payment.BalanceDue);
        Assert.Equal(SettlementAuthority.Canonical, payment.SettlementAuthorityState);
    }

    [Fact]
    public void CanonicalRentProjectionRejectsMixedLegacyChargeComponents()
    {
        var payment = NewPayment(900m);
        payment.RecordPayment("LEGACY", Guid.NewGuid(), PaymentStatus.Unpaid,
            elecAmount: 100m, fishKilos: 5m);
        payment.MarkSettlementPendingCutover();
        var at = DateTime.SpecifyKind(new DateTime(2026, 9, 26, 10, 0, 0), DateTimeKind.Utc);
        var cutover = CollectionSettlementCutover.Freeze(Guid.NewGuid(), CollectionSourceKind.PaymentRecord,
            payment.Id, null, payment.SettlementVersion, at, 900m, 0m, 900m,
            "{\"reconciled\":true}", Guid.NewGuid(), at.AddMinutes(1));
        payment.ActivateCanonicalSettlement(cutover);

        Assert.Throws<InvalidOperationException>(() =>
            payment.ApplyCanonicalRentProjection(100m, at.AddMinutes(2), "admin"));
    }
}
