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
    DateOnly? CountsMonthsFrom = null,
    // Each late rent month's surcharge and interest still owed, inside PenaltiesDue (ruled 2026-11-24).
    IReadOnlyList<LateChargeMonthDto>? LateCharges = null,
    // The rates in force today, for the labels only: a month already charged keeps the rates it was charged at.
    decimal? SurchargePercent = null,
    decimal? InterestPercent = null,
    // The present account this statement is of, so a screen holding an earlier term's row never shows it as that term's.
    Guid? ContractId = null,
    // Rent already taken for months after this one (paid ahead): carried to those months' bills, so it is stated here and never taken off TotalDue.
    int PaidAheadMonths = 0,
    decimal PaidAhead = 0m,
    DateOnly? PaidAheadThrough = null)
{
    public decimal PreviousBalance => ArrearsAmount + DelinquentAmount;

    public decimal Surcharges => LateCharges?.Sum(m => m.Surcharge) ?? 0m;

    public decimal Interest => LateCharges?.Sum(m => m.Interest) ?? 0m;

    /// <summary>Fines and penalties other than late months' surcharge and interest.</summary>
    public decimal OtherPenalties => PenaltiesDue - Surcharges - Interest;

    /// <summary>The unpaid rent of earlier months with its late charges: what is overdue before this month's rent.</summary>
    public decimal OverdueBalance => PreviousBalance + Surcharges + Interest;

    /// <summary>The part of the previous balance from before the billing rules begin. It is still on this statement, but it holds up nothing.</summary>
    public decimal BalanceBeforeRules => CountsMonthsFrom is { } from ? UnpaidMonths.Where(m => new DateOnly(m.Year, m.Month, 1) < from).Sum(m => m.Unpaid) : 0m;

    public decimal UtilityBalance => UnpaidUtilities.Sum(m => m.Unpaid);

    public decimal TotalDue => PreviousBalance + UtilityBalance + CurrentBalance + PenaltiesDue;
}

/// <summary>One late rent month's surcharge and interest still owed.</summary>
public sealed record LateChargeMonthDto(int Year, int Month, decimal Surcharge, decimal Interest);
