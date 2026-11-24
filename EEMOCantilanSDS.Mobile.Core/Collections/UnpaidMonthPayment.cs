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

    /// <summary>
    /// A part payment taken on the monthly sheet while earlier months are open: the rent of the oldest month first, each month cleared before the next,
    /// and what is left after the earlier months is this month's. A month that already has a part payment is only ever cleared, never part-paid again.
    /// </summary>
    public static (IReadOnlyList<UnpaidMonthCharge> Charges, decimal ThisMonth, string? Error) Spread(
        IReadOnlyList<MobileUnpaidMonthDto> earlier, decimal thisMonthBalance, decimal amount)
    {
        var months = earlier.OrderBy(Ordinal).ToList();
        var due = months.Sum(m => m.BalanceDue) + thisMonthBalance;
        if (amount <= 0m || decimal.Round(amount, 2) != amount) return ([], 0m, "Enter the amount received, up to two decimals.");
        if (amount > due) return ([], 0m, $"The amount cannot be more than {Peso(due)}.");

        var charges = new List<UnpaidMonthCharge>();
        var left = amount;
        var needed = 0m;
        foreach (var m in months)
        {
            needed += m.BalanceDue;
            if (left >= m.BalanceDue) { charges.Add(new(m.Year, m.Month, PaymentStatus.Paid, null, m.BalanceDue)); left -= m.BalanceDue; continue; }
            if (m.AmountPaid > 0m)
                return ([], 0m, $"{new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture)} is part-paid already; enter at least {Peso(needed)}.");
            charges.Add(new(m.Year, m.Month, PaymentStatus.Partial, left, left));
            return (charges, 0m, null);
        }
        return (charges, left, null);
    }

    private static string Peso(decimal amount) => "₱" + amount.ToString("N2", CultureInfo.InvariantCulture);

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
