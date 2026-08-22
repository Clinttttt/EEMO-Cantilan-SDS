using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What a part-settled month records, and where it stops being partial.
///
/// <para>
/// The Collection Manager can now set a month to Partial with the amount collected. Everything underneath already
/// existed: the command carries the amount, the record stores it, and the reports count it as paid and derive the
/// Partial status from the balance. These tests pin the rules that screen now depends on, because a figure that
/// reconciled differently from what the officer typed would be worse than no partial at all.
/// </para>
/// </summary>
public class PartialPaymentRecordTests
{
    private static PaymentRecord Month(decimal rent = 2_400m) =>
        PaymentRecord.Create(Guid.NewGuid(), 2026, 8, rent, "test");

    [Fact]
    public void APartialRecordsTheAmountHandedOver_AndOwesTheRest()
    {
        var month = Month(2_400m);

        month.UpdateStatus(PaymentStatus.Partial, 1_000m, updatedBy: "test");

        Assert.Equal(PaymentStatus.Partial, month.Status);
        Assert.Equal(1_000m, month.PartialAmount);
        Assert.Equal(1_000m, month.AmountPaid);      // what the reports count as collected
        Assert.Equal(1_400m, month.BalanceDue);      // and what is still owed
    }

    [Fact]
    public void AnAmountThatCoversTheWholeBill_IsRecordedAsPaid()
    {
        // The rule the drawer's hint states, so an officer who types the full rent is not left with a "partial" month
        // that owes nothing. Promotion clears the partial figure, because Paid already means the whole bill.
        var month = Month(2_400m);

        month.UpdateStatus(PaymentStatus.Partial, 2_400m, updatedBy: "test");

        Assert.Equal(PaymentStatus.Paid, month.Status);
        Assert.Equal(0m, month.PartialAmount);
        Assert.Equal(2_400m, month.AmountPaid);
        Assert.Equal(0m, month.BalanceDue);
    }

    [Fact]
    public void MoreThanTheBill_IsAlsoPaid_AndNeverOwesANegative()
    {
        var month = Month(2_400m);

        month.UpdateStatus(PaymentStatus.Partial, 3_000m, updatedBy: "test");

        Assert.Equal(PaymentStatus.Paid, month.Status);
        Assert.Equal(0m, month.BalanceDue);
    }

    [Fact]
    public void TheWholeBillIncludesUtilities_NotTheRentAlone()
    {
        // Why the drawer quotes the total rather than the base rent: the promotion compares against everything on the
        // record. Paying exactly the rent while electricity and water are also billed leaves the month partial, and owing.
        var month = Month(2_400m);
        month.RecordPayment("OR-1", Guid.NewGuid(), PaymentStatus.Partial,
            partialAmount: 2_400m, elecReading: 10m, elecAmount: 300m, waterReading: 2m, waterAmount: 100m,
            updatedBy: "test");

        Assert.Equal(2_800m, month.TotalBill);
        Assert.Equal(PaymentStatus.Partial, month.Status);
        Assert.Equal(400m, month.BalanceDue);
    }

    [Fact]
    public void MarkingAPartialMonthUnpaidAgain_LeavesNothingCollected()
    {
        // Correcting a mistake: the amount must not linger once the month is set back to owing, or the reports would
        // keep counting money the office no longer says it received.
        var month = Month(2_400m);
        month.UpdateStatus(PaymentStatus.Partial, 1_000m, updatedBy: "test");

        month.UpdateStatus(PaymentStatus.Unpaid, updatedBy: "test");

        Assert.Equal(PaymentStatus.Unpaid, month.Status);
        Assert.Equal(0m, month.PartialAmount);
        Assert.Equal(0m, month.AmountPaid);
        Assert.Equal(2_400m, month.BalanceDue);
    }
}
