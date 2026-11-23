using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// Every account of one facility as of today, each read exactly as that space's Statement of Account reads it (the same month walk, arrears walk,
/// utility bills and penalty sum), so a register row and the statement cannot disagree. Rent, metered utilities and penalties stay separate
/// liabilities; nothing here is posted or computed beyond adding them up.
/// </summary>
public sealed record BillingRegisterDto(
    FacilityCode Facility,
    DateOnly AsOf,
    // This month's due date under the office's billing rules; null for a day-billed market, which has none.
    DateOnly? DueDate,
    IReadOnlyList<BillingRegisterRowDto> Rows,
    // The late-charge rules in force today and their authority, stated on the sheet so its surcharge and interest columns can be checked.
    decimal? SurchargePercent = null,
    decimal? InterestPercent = null,
    int? InterestMaxMonths = null,
    string? Authority = null);

/// <param name="PreviousMonths">Fully elapsed months still unpaid (Arrears and Delinquent together), as the statement counts them.</param>
/// <param name="PreviousBalance">What those months still owe: the statement's PreviousBalance.</param>
/// <param name="Charges">This month's rent: the statement's CurrentCharges.</param>
/// <param name="Paid">Paid on this month so far: the statement's Payments.</param>
/// <param name="Balance">This month's rent still owed: the statement's CurrentBalance.</param>
public sealed record BillingRegisterRowDto(
    Guid StallId,
    string StallNo,
    string Occupant,
    string Section,
    int PreviousMonths,
    decimal PreviousBalance,
    decimal Charges,
    decimal Paid,
    string? ORNumber,
    decimal Balance,
    decimal UtilityBalance,
    decimal PenaltiesDue,
    // The late months' surcharge and interest inside PenaltiesDue (ruled 2026-11-24).
    decimal Surcharge = 0m,
    decimal Interest = 0m,
    // Rent already taken for months after this one: the statement's PaidAhead, carried to those bills and never part of TotalDue.
    int PaidAheadMonths = 0,
    decimal PaidAhead = 0m,
    DateOnly? PaidAheadThrough = null)
{
    /// <summary>The statement's TotalDue: previous balance, utilities, this month's balance and penalties.</summary>
    public decimal TotalDue => PreviousBalance + UtilityBalance + Balance + PenaltiesDue;

    /// <summary>Fines and penalties other than late months' surcharge and interest, inside <see cref="PenaltiesDue"/>.</summary>
    public decimal OtherPenalties => PenaltiesDue - Surcharge - Interest;
}
