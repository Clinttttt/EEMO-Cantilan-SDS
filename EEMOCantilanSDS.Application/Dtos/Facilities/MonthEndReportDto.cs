using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// A single comprehensive month-end snapshot across all eight facilities for one billing month.
/// Rental facilities (NPM/TCC/NCC/BBQ/ICE) carry per-payor compliance rows that reconcile to the
/// facility subtotal; transaction facilities (SLH/TRM/TPM) carry collected-summary figures only
/// (they have no recurring per-payor balance). Grand totals are the sum of the per-facility figures.
/// </summary>
public record MonthEndReportDto(
    int Year,
    int Month,
    string PeriodLabel,
    decimal TotalCollected,
    decimal TotalOutstanding,
    int OverallCollectionRate,
    int TotalPaidCount,
    int TotalPartialCount,
    int TotalUnpaidCount,
    IReadOnlyList<MonthEndFacilityDto> Facilities
);

public record MonthEndFacilityDto(
    FacilityCode Code,
    string Name,
    bool IsRental,
    decimal Collected,
    decimal Outstanding,
    int CollectionRate,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int TotalPayors,
    IReadOnlyList<MonthEndPayorDto> Payors,
    IReadOnlyList<MonthEndTxnPayorDto> TransactionPayors,
    // The market's metered utilities for the month, per space. Empty for every other facility, and empty for the
    // market itself when no bill was raised.
    IReadOnlyList<MonthEndUtilityRowDto>? Utilities = null
);

public record MonthEndPayorDto(
    string StallNo,
    string Payor,
    decimal MonthlyRate,
    string Status,
    decimal AmountPaid,
    decimal Balance,
    string? ORNumber,
    // NPM is billed daily (not monthly); the report shows the daily rate for that facility.
    decimal DailyRate = 0m,
    // NPM-only, additive: the fixed full-month (30-day) coverage reference (₱900) and the remaining
    // balance toward it (coverage − amount paid, never below zero). These are display-only extras shown
    // alongside the existing daily-based Balance — they do not alter any existing collection logic and
    // stay 0 for every non-NPM facility.
    decimal MonthlyCoverage = 0m,
    decimal MonthlyCoverageBalance = 0m,
    // Which area of the market this space is in, so a market sheet can be read area by area. Carries the CANONICAL
    // wording for a canonical section, which the portal maps to the office's own name for it, and the office's own
    // custom name for a section it invented. Empty for a facility that has no areas.
    string Section = "",
    // Fish kilos weighed for this space over the month (the market's fish area only; 0 everywhere else).
    decimal FishKilos = 0m
);

/// <summary>
/// One payor of a transaction facility (SLH owner / TRM driver / TPM vendor) for the month, with their
/// individual records grouped underneath. Repeated payors collapse into a single expandable row whose
/// total reconciles to the facility subtotal.
/// </summary>
/// <summary>
/// One space's electricity and water for the month: what each utility charged, what was collected against it, and the
/// receipt it was collected on. Shown as its own table, because a utility is not a stall fee: it is metered, it is
/// billed per reading, and the office reconciles it separately.
/// </summary>
public record MonthEndUtilityRowDto(
    string StallNo,
    string Payor,
    decimal ElecCharge,
    decimal ElecPaid,
    decimal WaterCharge,
    decimal WaterPaid,
    string? ORNumber
)
{
    public decimal Charged => ElecCharge + WaterCharge;
    public decimal Collected => ElecPaid + WaterPaid;
    /// <summary>What is still owed, per utility, so an overpayment on one cannot mask a shortfall on the other.</summary>
    public decimal Balance => Math.Max(0m, ElecCharge - ElecPaid) + Math.Max(0m, WaterCharge - WaterPaid);
}

public record MonthEndTxnPayorDto(
    string Payor,
    int RecordCount,
    decimal TotalCollected,
    IReadOnlyList<MonthEndTxnRecordDto> Records,
    string? Summary = null,
    // Domain quantity for the facility's context column: SLH = total heads, TRM = trips, TPM = Fridays.
    int Quantity = 0
);

public record MonthEndTxnRecordDto(
    string Date,
    string Description,
    decimal Amount,
    string? ORNumber
);
