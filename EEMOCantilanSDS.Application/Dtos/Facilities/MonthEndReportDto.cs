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
    IReadOnlyList<MonthEndTxnPayorDto> TransactionPayors
);

public record MonthEndPayorDto(
    string StallNo,
    string Payor,
    decimal MonthlyRate,
    string Status,
    decimal AmountPaid,
    decimal Balance,
    string? ORNumber
);

/// <summary>
/// One payor of a transaction facility (SLH owner / TRM driver / TPM vendor) for the month, with their
/// individual records grouped underneath. Repeated payors collapse into a single expandable row whose
/// total reconciles to the facility subtotal.
/// </summary>
public record MonthEndTxnPayorDto(
    string Payor,
    int RecordCount,
    decimal TotalCollected,
    IReadOnlyList<MonthEndTxnRecordDto> Records,
    string? Summary = null
);

public record MonthEndTxnRecordDto(
    string Date,
    string Description,
    decimal Amount,
    string? ORNumber
);
