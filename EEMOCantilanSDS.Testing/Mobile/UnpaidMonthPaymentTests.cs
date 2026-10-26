using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Collections;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class UnpaidMonthPaymentTests
{
    private static MobileUnpaidMonthDto Month(int month, decimal bill = 2400m, decimal paid = 0m) => new(2026, month, bill, paid, bill - paid);

    [Fact]
    public void Several_months_are_each_collected_in_full_and_never_spread()
    {
        var (charges, error) = UnpaidMonthPayment.Plan([Month(9), Month(7), Month(8, paid: 400m)], amountText: "1");   // any typed amount is ignored for several months
        Assert.Null(error);
        Assert.Equal([7, 8, 9], charges.Select(c => c.Month));                                                          // oldest first
        Assert.All(charges, c => { Assert.Equal(PaymentStatus.Paid, c.Status); Assert.Null(c.PartialAmount); });
        Assert.Equal([2400m, 2000m, 2400m], charges.Select(c => c.Amount));                                              // each month's own remaining balance
    }

    [Fact]
    public void One_month_takes_a_full_or_a_part_payment_only_when_nothing_was_paid_on_it()
    {
        var (full, _) = UnpaidMonthPayment.Plan([Month(8)], "2400");
        Assert.Equal((PaymentStatus.Paid, (decimal?)null), (full[0].Status, full[0].PartialAmount));

        var (part, _) = UnpaidMonthPayment.Plan([Month(8)], "1000.50");
        Assert.Equal((PaymentStatus.Partial, (decimal?)1000.50m), (part[0].Status, part[0].PartialAmount));

        var (_, partOnPartial) = UnpaidMonthPayment.Plan([Month(8, paid: 400m)], "500");
        Assert.Contains("full remaining balance", partOnPartial);
        var (rest, _) = UnpaidMonthPayment.Plan([Month(8, paid: 400m)], "2000");
        Assert.Equal(PaymentStatus.Paid, rest[0].Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.555")]
    [InlineData("2400.01")]
    public void A_bad_or_excessive_amount_is_refused_and_nothing_is_planned(string text)
    {
        var (charges, error) = UnpaidMonthPayment.Plan([Month(8)], text);
        Assert.Empty(charges);
        Assert.NotNull(error);
    }

    [Fact]
    public void Nothing_chosen_plans_nothing()
    {
        var (charges, error) = UnpaidMonthPayment.Plan([], "100");
        Assert.Empty(charges); Assert.NotNull(error);
    }
}
