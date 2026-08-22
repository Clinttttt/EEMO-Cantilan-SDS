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
    // NOTE: these two lists are CAPPED for display (the most overdue accounts first). Never count or sum them to state
    // how many accounts need follow-up or how much is owed — use the four totals at the end of this record, which are
    // counted over every account.
    IReadOnlyList<AttentionAccountDto> Delinquent,
    IReadOnlyList<AttentionAccountDto> Arrears,

    // ── Trend (chronological; selected period flagged) ──
    IReadOnlyList<ReportTrendPointDto> Trend,
    decimal YtdCollected,

    // ── Facility breakdown ──
    IReadOnlyList<FinancialFacilityRowDto> Facilities,

    // ── Traceability ──
    IReadOnlyList<FinancialRecordDto> RecentRecords,

    // ── Closed / expired accounts with an outstanding historical balance ──
    // The Closed Accounts register total (facility-scoped, all-time). Kept SEPARATE from current
    // delinquency by design: these are INACTIVE accounts (frozen or contract lapsed), not current
    // delinquents. Surfaced here only for visibility/follow-up.
    int ClosedWithBalanceCount = 0,
    decimal ClosedWithBalanceOutstanding = 0m,
    /// <summary>
    /// The month the attention figures are counted UP TO — the last month of the report's own period that has
    /// closed. Carried on the DTO because the page cannot derive it: it was naming the month from today's date, so a
    /// 2024 report read "counted to July 2026". Empty when there is nothing to attend to.
    /// </summary>
    string AttentionSpanLabel = "",

    // ── The TRUE follow-up figures ───────────────────────────────────────────────────────────────────────────────
    // Counted over EVERY account, which <see cref="Delinquent"/> and <see cref="Arrears"/> cannot do: those are capped
    // at the most overdue accounts so the payload stays bounded. The report header used to count and sum the capped
    // lists and label the result "outstanding in full", so an office with more accounts than the cap was shown fewer
    // accounts and less money than it was owed, on a printed report that claimed to be complete.

    /// <summary>Every account with 3 or more unpaid months, not only those listed in <see cref="Delinquent"/>.</summary>
    int DelinquentAccountsTotal = 0,

    /// <summary>What all of those accounts owe in full.</summary>
    decimal DelinquentOutstandingTotal = 0m,

    /// <summary>Every account with 1–2 unpaid months, not only those listed in <see cref="Arrears"/>.</summary>
    int ArrearsAccountsTotal = 0,

    /// <summary>What all of those accounts owe in full.</summary>
    decimal ArrearsOutstandingTotal = 0m
);

/// <summary>A payor needing follow-up. <see cref="UnpaidMonths"/> drives delinquent vs arrears bucketing, and
/// <see cref="TermLapsed"/> marks an account whose term has run out while the space was never handed over — still
/// collected, but the office needs to see that it also wants renewing.</summary>
public record AttentionAccountDto(
    string Name,
    FacilityCode FacilityCode,
    string StallNo,
    string Location,
    decimal Balance,
    int UnpaidMonths,
    bool TermLapsed = false,
    Guid? StallId = null
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
/// The fee components (<see cref="DailyFeeCollected"/> + <see cref="FishCollected"/> + <see cref="ElecCollected"/> +
/// <see cref="WaterCollected"/> + any remainder from monthly payments) reconcile back to the row's total Collected.
/// Full-month coverage is the fixed 30-day ₱900 reference summed per occupied stall; its balance is summed per stall as
/// max(0, ₱900 − that stall's amount paid) — identical to the Month-End report. <see cref="PeriodBalance"/>
/// is the selected period's assessed STALL-FEE obligation minus collected (whole-period, e.g. the full month or
/// full year); the row's "Unpaid (period)" column is that plus <see cref="UtilityOutstanding"/>, which the expandable
/// row states in its own "Utilities Due" panel rather than inside Outstanding.
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
    decimal ExcusedAmount = 0m,
    // NPM electricity + water collected this period, and the combined outstanding utility balance. Both are COUNTED
    // in the row's Collected and Unpaid: the office states that the market's electricity and water are the market's
    // revenue. They live on utility bills, which no stall-fee path writes to, so counting them adds nothing twice.
    // Zero on a Weekly report, where a bill billed for a month carries no week of its own. 0 = none.
    decimal ElecCollected = 0m,
    decimal WaterCollected = 0m,
    decimal UtilityOutstanding = 0m
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
