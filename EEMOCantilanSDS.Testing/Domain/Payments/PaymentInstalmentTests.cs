using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// IA-081: a month paid in more than one receipt keeps every receipt. Finishing a partial month must never move the first
/// receipt's money into the finishing month, nor overwrite its OR, date or collector.
/// </summary>
public class PaymentInstalmentTests
{
    private static PaymentRecord Month(decimal rent = 2_400m) => PaymentRecord.Create(Guid.NewGuid(), 2026, 10, rent, "test");

    private static (decimal Amount, string? Or, Guid? Collector)[] Receipts(PaymentRecord p) =>
        p.Instalments.Select(i => (i.Amount, i.ORNumber, i.CollectorId)).ToArray();

    [Fact]
    public void A_month_paid_in_one_receipt_keeps_no_list_and_reads_as_before()
    {
        var month = Month();
        month.RecordPayment("OR-1", Guid.NewGuid(), PaymentStatus.Paid, updatedBy: "test");
        Assert.Empty(month.Instalments);

        var corrected = Month();
        corrected.UpdateStatus(PaymentStatus.Partial, 1_000m, updatedBy: "test");
        corrected.UpdateStatus(PaymentStatus.Partial, 800m, updatedBy: "correction"); // lowered, never a second receipt
        Assert.Empty(corrected.Instalments);
    }

    [Fact]
    public void Finishing_a_partial_month_keeps_the_first_receipt_and_adds_the_rest_as_its_own()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var month = Month();
        month.RecordPayment("OR-1", first, PaymentStatus.Partial, 1_000m, updatedBy: "test");
        var firstAt = month.PaidAt;

        month.RecordPayment("OR-2", second, PaymentStatus.Paid, updatedBy: "test");

        Assert.Equal([(1_000m, "OR-1", first), (1_400m, "OR-2", second)], Receipts(month));
        Assert.Equal(firstAt, month.Instalments.First().ReceivedAt);
        Assert.Equal(month.AmountPaid, month.Instalments.Sum(i => i.Amount));
        Assert.Equal("OR-2", month.ORNumber); // the record shows the newest receipt
    }

    [Fact]
    public void A_cumulative_partial_adds_only_what_is_new()
    {
        var month = Month();
        month.RecordPayment("OR-1", Guid.NewGuid(), PaymentStatus.Partial, 1_000m, updatedBy: "test");
        month.RecordPayment("OR-2", Guid.NewGuid(), PaymentStatus.Partial, 1_800m, updatedBy: "test");
        month.RecordPayment("OR-3", Guid.NewGuid(), PaymentStatus.Paid, updatedBy: "test");

        Assert.Equal([1_000m, 800m, 600m], month.Instalments.Select(i => i.Amount));
        Assert.Equal(["OR-1", "OR-2", "OR-3"], month.Instalments.Select(i => i.ORNumber));
    }

    [Fact]
    public void An_OR_set_after_the_status_belongs_to_the_receipt_just_taken()
    {
        var month = Month();
        month.UpdateStatus(PaymentStatus.Partial, 1_000m, updatedBy: "test", collectorId: Guid.NewGuid());
        month.SetOrNumber("OR-1", "test");
        month.UpdateStatus(PaymentStatus.Paid, updatedBy: "test", collectorId: Guid.NewGuid());
        month.SetOrNumber("OR-2", "test");

        Assert.Equal(["OR-1", "OR-2"], month.Instalments.Select(i => i.ORNumber));
    }

    [Fact]
    public void A_correction_lowers_the_newest_receipt_first_and_keeps_every_OR()
    {
        var month = Month();
        month.RecordPayment("OR-1", Guid.NewGuid(), PaymentStatus.Partial, 1_000m, updatedBy: "test");
        month.RecordPayment("OR-2", Guid.NewGuid(), PaymentStatus.Partial, 1_800m, updatedBy: "test");

        month.UpdateStatus(PaymentStatus.Partial, 1_500m, updatedBy: "correction");
        Assert.Equal([1_000m, 500m], month.Instalments.Select(i => i.Amount));

        month.MarkUnpaid("correction");
        Assert.Equal([0m, 0m], month.Instalments.Select(i => i.Amount));
        Assert.Equal(["OR-1", "OR-2"], month.Instalments.Select(i => i.ORNumber)); // an issued OR stays recorded
    }

    [Fact]
    public void Receipts_are_placed_rent_first_and_always_add_up_to_the_fee_part()
    {
        // Rent 2,000 and weighing 300: the first receipt is rent, the newest carries the rest of the rent and the weighing.
        var placed = CollectorFeeMoney.PlaceReceipts(2_300m, 2_000m, [("Oct", 1_000m), ("Nov", 1_500m)]);
        Assert.Equal([("Oct", 1_000m, 0m), ("Nov", 1_000m, 300m)], placed);

        // A receipt corrected to nothing is skipped; a partial fee part is never exceeded.
        placed = CollectorFeeMoney.PlaceReceipts(1_500m, 2_400m, [("a", 1_000m), ("b", 0m), ("c", 500m)]);
        Assert.Equal([("a", 1_000m, 0m), ("c", 500m, 0m)], placed);

        // Earlier receipts that already cover the fee part leave nothing for the newest, which is then not placed.
        placed = CollectorFeeMoney.PlaceReceipts(2_000m, 2_000m, [("a", 2_500m), ("b", 300m)]);
        Assert.Equal([("a", 2_000m, 0m)], placed);
    }
}
