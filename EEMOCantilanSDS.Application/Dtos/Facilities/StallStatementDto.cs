namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// One stall's present account read as a statement. The previous balance is the money Follow-up and the Financial Reports state for the same
/// account, read from the same whole-account walk and never recomputed. It is called "previous balance" because Arrears and Delinquent are
/// reserved words (IA-019, IA-020, IA-069) and this is the two together. An earlier term's balance stays in the Closed / Inactive register.
/// </summary>
public sealed record StallStatementDto(
    DateOnly AsOf,
    int ArrearsMonths,
    decimal ArrearsAmount,
    int DelinquentMonths,
    decimal DelinquentAmount,
    decimal CurrentCharges,
    decimal Payments,
    decimal CurrentBalance,
    IReadOnlyList<UnpaidMonthDto> UnpaidMonths,
    // Metered electricity and water: bills of their own, settled apart from rent, so never inside the rent figures above.
    IReadOnlyList<UnpaidMonthDto> UnpaidUtilities,
    // Fines and late penalties assessed on this account, less what has been collected on them. A liability of its own, never part of the rent.
    decimal PenaltiesDue = 0m,
    // This month's due date under the office's billing rules. Null for a day-billed space, which has none.
    DateOnly? CurrentDueDate = null,
    // The month the office's billing rules begin at (IA-075).
    DateOnly? CountsMonthsFrom = null)
{
    public decimal PreviousBalance => ArrearsAmount + DelinquentAmount;

    /// <summary>The part of the previous balance from before the billing rules begin. It is still on this statement, but it holds up nothing.</summary>
    public decimal BalanceBeforeRules => CountsMonthsFrom is { } from ? UnpaidMonths.Where(m => new DateOnly(m.Year, m.Month, 1) < from).Sum(m => m.Unpaid) : 0m;

    public decimal UtilityBalance => UnpaidUtilities.Sum(m => m.Unpaid);

    public decimal TotalDue => PreviousBalance + UtilityBalance + CurrentBalance + PenaltiesDue;
}
