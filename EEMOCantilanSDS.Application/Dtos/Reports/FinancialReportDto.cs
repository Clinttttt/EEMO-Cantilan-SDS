using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Reports;

/// <summary>
/// Single aggregate payload for the admin Financial Reports page. Composed from the canonical
/// per-facility report aggregation (stall facilities) and the transaction facilities (SLH/TRM/TPM),
/// so it reconciles to the same figures used by the Month-End report. All money is in PHP.
///
/// Honest-measure notes:
///  • <see cref="CollectionRatePct"/> is amount-based: Collected / Billed (Collected + CurrentPeriodUnpaid).
///  • Per-head (SLH) / per-trip (TRM) / weekly-attendance (TPM) facilities are paid on service: they
///    contribute to Collected but carry no recurring unpaid balance (<see cref="FinancialFacilityRowDto.Unpaid"/> = null).
///  • Delinquent = 3+ unpaid months; arrears = 1–2 unpaid months (kept in separate lists).
/// </summary>
public record FinancialReportDto(
    // ── Scope / identity ──
    string PeriodLabel,
    string ScopeLabel,
    string Frequency,
    int FacilityCount,

    // ── Financial position (KPIs) ──
    decimal Collected,
    decimal CurrentPeriodUnpaid,
    decimal Billed,
    int CollectionRatePct,
    int PaidRecords,
    int ExpectedRecords,
    decimal? CollectedPreviousPeriod,
    string? PreviousPeriodLabel,

    // ── Attention & follow-up ──
    IReadOnlyList<AttentionAccountDto> Delinquent,
    IReadOnlyList<AttentionAccountDto> Arrears,

    // ── Trend (chronological; selected period flagged) ──
    IReadOnlyList<ReportTrendPointDto> Trend,
    decimal YtdCollected,

    // ── Facility breakdown ──
    IReadOnlyList<FinancialFacilityRowDto> Facilities,

    // ── Traceability ──
    IReadOnlyList<FinancialRecordDto> RecentRecords
);

/// <summary>A payor needing follow-up. <see cref="UnpaidMonths"/> drives delinquent vs arrears bucketing.</summary>
public record AttentionAccountDto(
    string Name,
    FacilityCode FacilityCode,
    string StallNo,
    string Location,
    decimal Balance,
    int UnpaidMonths
);

public record ReportTrendPointDto(
    string Label,
    int Year,
    int Month,
    decimal Collected,
    decimal Unpaid,
    bool IsSelected
);

/// <summary>
/// One facility row in the breakdown. <see cref="Unpaid"/> and <see cref="RatePct"/> are null for
/// paid-on-service facilities (no recurring balance/rate). <see cref="PaidOnService"/> makes that explicit.
/// <see cref="Detail"/> carries facility-specific extras (currently NPM only) for an expandable row;
/// null for facilities with no extra breakdown.
/// </summary>
public record FinancialFacilityRowDto(
    FacilityCode Code,
    string Name,
    string Model,
    bool PaidOnService,
    decimal Collected,
    decimal? Unpaid,
    int PaidRecords,
    int? RatePct,
    string Status,
    NpmFacilityDetailDto? Detail = null
);

/// <summary>
/// NPM-only breakdown shown in an expandable row, so the generic facility table stays uncluttered.
/// The fee components (<see cref="DailyFeeCollected"/> + <see cref="FishCollected"/> + the implied
/// utilities/other remainder) reconcile back to the row's total Collected. Full-month coverage is the
/// fixed 30-day ₱900 reference summed per occupied stall; its balance is summed per stall as
/// max(0, ₱900 − that stall's amount paid) — identical to the Month-End report. <see cref="PeriodBalance"/>
/// is the selected period's assessed obligation minus collected (whole-period, e.g. the full month or
/// full year), the same value shown in the table's "Unpaid (period)" column.
/// </summary>
public record NpmFacilityDetailDto(
    decimal DailyFeeCollected,
    decimal FishCollected,
    decimal FishKilos,
    decimal PeriodBalance,
    decimal FullMonthCoverage,
    decimal FullMonthCoverageBalance,
    // Total excused/absent amount for the period (Σ absent days × ₱30). Absent days are not owed, so
    // they reduce the full-month coverage; this line makes that deduction explicit. 0 = none.
    decimal ExcusedAmount = 0m
);

public record FinancialRecordDto(
    string Reference,
    string Payor,
    FacilityCode FacilityCode,
    string StallNo,
    DateTime RecordedAt,
    string? Collector,
    string Method,
    decimal Amount
);
