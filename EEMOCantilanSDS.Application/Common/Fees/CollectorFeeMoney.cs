using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Fees;

/// <summary>
/// What counts as FEE money in a collector's hands.
///
/// <para>
/// The office banks electricity and water separately as additional income, so neither belongs in a collector's fee
/// accountability: not in the ceiling a remittance is checked against, and not in the total the Report of Collections
/// states. Both read this rule, because a document whose summary and reconciliation disagree is worse than one that states
/// a single figure and says what it excludes.
/// </para>
/// </summary>
public static class CollectorFeeMoney
{
    /// <summary>
    /// The fee part of a monthly bill's payment.
    ///
    /// <para>
    /// A full payment contributes the rent and any fish fee. A part payment carries no split of its own, so it is applied to
    /// the fee charge first and capped there: the figure can then never claim more fee money than the fees came to, and the
    /// excess belongs to the utilities.
    /// </para>
    /// </summary>
    public static decimal MonthlyFeePortion(PaymentStatus status, decimal baseRental, decimal? fishKilos, decimal partialAmount, decimal fishRatePerKilo = 1.00m)
    {
        var feeCharge = baseRental + ((fishKilos ?? 0m) * fishRatePerKilo);
        return status == PaymentStatus.Partial
            ? Math.Min(partialAmount, feeCharge)
            : feeCharge;
    }

    /// <summary>
    /// Where the fee part of a month paid in more than one receipt came in (IA-081): receipt by receipt, oldest first, rent before
    /// weighing, never more than <paramref name="portion"/> in all. The newest receipt carries whatever the earlier ones leave, so
    /// the placed parts always add up to the fee part and no money is dropped or counted twice.
    /// </summary>
    public static IReadOnlyList<(T Receipt, decimal Rent, decimal Weighing)> PlaceReceipts<T>(
        decimal portion, decimal baseRental, IEnumerable<(T Receipt, decimal Amount)> receiptsOldestFirst)
    {
        var parts = receiptsOldestFirst.Where(r => r.Amount > 0m).ToList();
        var placed = new List<(T, decimal, decimal)>(parts.Count);
        var before = 0m;
        for (var i = 0; i < parts.Count; i++)
        {
            var after = i == parts.Count - 1 ? portion : Math.Min(portion, before + parts[i].Amount);
            if (after <= before) continue;
            var rent = Math.Min(after, baseRental) - Math.Min(before, baseRental);
            placed.Add((parts[i].Receipt, rent, after - before - rent));
            before = after;
        }
        return placed;
    }
}
