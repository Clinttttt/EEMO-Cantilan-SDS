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

    /// <summary>
    /// Every account with a balance, grouped by how long it has been owed. Counted over the WHOLE account set, never over
    /// the capped lists above — which is the point of computing it server-side rather than bucketing what the page happens
    /// to be showing.
    /// </summary>
    /// <remarks>
    /// It answers a question the two lists cannot: whether the office is looking at a lot of recent arrears or at a few
    /// debts that have been outstanding for years. The same ₱50,000 means very different things in those two cases.
    /// </remarks>
    IReadOnlyList<ReceivableAgingBandDto> Aging,

    /// <summary>
    /// Accounts whose contract term has run out while the occupant remains in the space, and what they owe.
    /// </summary>
    /// <remarks>
    /// Not a separate debt: these accounts are already inside the delinquent and arrears figures, because the office keeps
    /// collecting from a lapsed occupancy and the register is explicit that it is still being billed. This states how much
    /// of that money sits behind a term that has expired — an exposure the office can act on by renewing — so it must be
    /// read as a slice of the total and never added to it.
    /// </remarks>
    int LapsedWithBalanceCount,
    decimal LapsedWithBalanceOutstanding,

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

/// <summary>
/// One band of the receivable aging schedule: how many accounts have been owing for this long, and what they owe.
/// </summary>
/// <param name="Label">The band as the office reads it, e.g. "1–2 months" or "12+ months".</param>
/// <param name="Accounts">Accounts whose unpaid-month count falls in this band, over the WHOLE set.</param>
/// <param name="Outstanding">What those accounts owe in full.</param>
/// <remarks>
/// The bands partition every account with at least one unpaid month, so their counts sum to the delinquent and arrears
/// totals combined and their amounts to those two amounts combined. Nothing is double-counted and nothing falls between
/// bands, which is what makes the schedule safe to read beside the totals rather than instead of them.
/// </remarks>
public record ReceivableAgingBandDto(
    string Label,
    int Accounts,
    decimal Outstanding
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
    /// <summary>
    /// How many records the period expected from this facility, so the paid count reads as coverage rather than as a bare
    /// number. Computed per facility all along and then discarded before it reached the page: the office could see that 284
    /// records were paid but not whether that was out of 290 or out of 400.
    /// </summary>
    /// <remarks>Zero for a paid-on-service facility, which has no roll to be measured against.</remarks>
    int ExpectedRecords,
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
    decimal Amount,

    // The stall the row is about, so the payor's name can link to the right profile. StallNo above cannot do it: the
    // market numbers spaces per section, so one facility holds several "Stall 1" and a facility-and-number link opens
    // whichever the lookup finds first — the fault fixed in the follow-up queue and closed accounts (7d4b2bc2), which
    // this feed still had. Null for rows about no stall (slaughter, terminal trips, market-day vendors).
    Guid? StallId = null
);
