using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// Read-only mobile reporting snapshot for the authenticated collector's assigned facilities.
/// Collection totals come from recorded transactions; open balances come from the facility collection
/// state for the selected reporting month.
/// </summary>
public sealed record MobileCollectorReportDto(
    int Year,
    int Month,
    DateOnly FromDate,
    DateOnly ToDate,
    bool DailyReportMode,
    MobileReportTotalsDto Totals,
    IReadOnlyList<MobileReportFacilitySummaryDto> Facilities,
    IReadOnlyList<MobileReportPeriodSummaryDto> Periods,
    IReadOnlyList<MobileReportPayeeSummaryDto> Payees,
    IReadOnlyList<MobileReportTransactionDto> Transactions,
    IReadOnlyList<MobileReportAbsentExcusedDto> AbsentExcused,
    // Electricity & water billing for the reporting month — kept SEPARATE from the collection totals
    // above (utilities are a distinct meter-based charge, not the stall/daily fee). Null when the
    // collector's facilities don't include NPM or no utility bill exists for the month.
    MobileReportUtilitySummaryDto? Utility = null);

/// <summary>Miscellaneous (electricity &amp; water) billing summary for the reporting month.</summary>
public sealed record MobileReportUtilitySummaryDto(
    decimal TotalCharge,
    decimal TotalCollected,
    decimal TotalOutstanding,
    int BillCount,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    IReadOnlyList<MobileReportUtilityPayeeDto> Payees);

/// <summary>One payor's utility bill for the month (electricity &amp; water settle independently).</summary>
public sealed record MobileReportUtilityPayeeDto(
    string PayorName,
    string? StallNo,
    string ElecStatus,
    string WaterStatus,
    string OverallStatus,
    decimal TotalCharge,
    decimal AmountPaid,
    decimal Balance);

/// <summary>One recorded collection event — backs the "Total Collected" / "Paid Payors" detail views.</summary>
public sealed record MobileReportTransactionDto(
    FacilityCode FacilityCode,
    string FacilityName,
    string? StallNo,
    string PayorName,
    DateOnly CollectionDate,
    decimal Amount,
    bool IsPartial,
    string? ORNumber,
    bool IsAdminRecorded = false);

/// <summary>
/// One excused record — backs the "Absent / Excused" detail view. It is NPM per-day absence or a
/// monthly-rental exception; either way the payor owes ₱0 and is never treated as unpaid.
/// </summary>
public sealed record MobileReportAbsentExcusedDto(
    FacilityCode FacilityCode,
    string FacilityName,
    string? StallNo,
    string PayorName,
    DateOnly Date,
    string Source,
    string? Reason);

public sealed record MobileReportTotalsDto(
    decimal CollectedAmount,
    decimal OutstandingAmount,
    int TransactionCount,
    int PayeeCount,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int AbsentExcusedCount,
    int AssignedFacilityCount,
    // Of the CollectedAmount/TransactionCount above, the portion recorded at the office/by an admin
    // (CollectorId == null) rather than by this collector — so the mobile can show "collected by you"
    // (CollectedAmount − OfficeCollectedAmount) with "recorded at office" as a separate line.
    decimal OfficeCollectedAmount = 0m,
    int OfficeTransactionCount = 0);

public sealed record MobileReportFacilitySummaryDto(
    FacilityCode FacilityCode,
    string FacilityName,
    bool IsDaily,
    decimal CollectedAmount,
    decimal OutstandingAmount,
    int TransactionCount,
    int PayeeCount,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int AbsentExcusedCount);

public sealed record MobileReportPeriodSummaryDto(
    DateOnly PeriodDate,
    decimal CollectedAmount,
    int TransactionCount,
    int PayeeCount,
    int PartialCount,
    int OpenItemCount,
    int AbsentExcusedCount);

public sealed record MobileReportPayeeSummaryDto(
    string PayorName,
    string? ContractName,
    FacilityCode FacilityCode,
    string FacilityName,
    string? StallNo,
    string AreaLabel,
    PaymentStatus Status,
    decimal AssessedAmount,
    decimal AmountPaid,
    decimal Balance,
    int TransactionCount,
    int PartialCount,
    DateTime? LastCollectedAt,
    string? ORNumber);
