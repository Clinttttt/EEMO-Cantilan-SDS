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
}
