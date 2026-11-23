using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>One month's payment as it is sent to the office: the month, whether it clears the month, and the amount when it is a part payment.</summary>
public sealed record UnpaidMonthCharge(int Year, int Month, PaymentStatus Status, decimal? PartialAmount, decimal Amount);

/// <summary>
/// What a collector's choice of unpaid months turns into. Several months are always collected in full, each as its own monthly record - nothing is spread across
/// months for them. A single month may take a part payment, only when nothing has been paid on it yet (the monthly record keeps what was received, not a running total).
/// </summary>
public static class UnpaidMonthPayment
{
    private static int Ordinal(MobileUnpaidMonthDto m) => m.Year * 12 + m.Month;

    /// <summary>
    /// What a tap picks when the office takes the oldest month first: the tapped months and every earlier still-open month the server marks
    /// <see cref="MobileUnpaidMonthDto.InOrder"/>, so the picker never holds a gap the office would refuse. Months outside the order are only the ones tapped.
    /// </summary>
    public static IEnumerable<MobileUnpaidMonthDto> WithEarlier(IEnumerable<MobileUnpaidMonthDto> open, IReadOnlyCollection<MobileUnpaidMonthDto> tapped) =>
        open.Where(m => tapped.Contains(m) || m.InOrder && tapped.Any(t => t.InOrder && Ordinal(m) < Ordinal(t)));

    /// <summary>What dropping these months drops: them and every later chosen month marked <see cref="MobileUnpaidMonthDto.InOrder"/>.</summary>
    public static IEnumerable<MobileUnpaidMonthDto> WithLater(IEnumerable<MobileUnpaidMonthDto> chosen, IReadOnlyCollection<MobileUnpaidMonthDto> tapped) =>
        chosen.Where(m => tapped.Contains(m) || m.InOrder && tapped.Any(t => t.InOrder && Ordinal(m) > Ordinal(t)));

    public static (IReadOnlyList<UnpaidMonthCharge> Charges, string? Error) Plan(IReadOnlyList<MobileUnpaidMonthDto> selected, string? amountText)
    {
        if (selected.Count == 0) return ([], "Choose at least one month.");
        if (selected.Count > 1)
            return (selected.OrderBy(m => m.Year).ThenBy(m => m.Month)
                .Select(m => new UnpaidMonthCharge(m.Year, m.Month, PaymentStatus.Paid, null, m.BalanceDue)).ToArray(), null);

        var month = selected[0];
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0m || decimal.Round(amount, 2) != amount)
            return ([], "Enter the amount received, up to two decimals.");
        if (amount > month.BalanceDue) return ([], $"The amount cannot be more than ₱{month.BalanceDue.ToString("N2", CultureInfo.InvariantCulture)}.");
        if (amount == month.BalanceDue) return ([new(month.Year, month.Month, PaymentStatus.Paid, null, amount)], null);
        if (month.AmountPaid > 0m) return ([], $"Enter the full remaining balance, ₱{month.BalanceDue.ToString("N2", CultureInfo.InvariantCulture)}.");
        return ([new(month.Year, month.Month, PaymentStatus.Partial, amount, amount)], null);
    }
}
