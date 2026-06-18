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
    IReadOnlyList<MobileReportPayeeSummaryDto> Payees);

public sealed record MobileReportTotalsDto(
    decimal CollectedAmount,
    decimal OutstandingAmount,
    int TransactionCount,
    int PayeeCount,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    int AssignedFacilityCount);

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
    int UnpaidCount);

public sealed record MobileReportPeriodSummaryDto(
    DateOnly PeriodDate,
    decimal CollectedAmount,
    int TransactionCount,
    int PayeeCount,
    int PartialCount,
    int OpenItemCount);

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
