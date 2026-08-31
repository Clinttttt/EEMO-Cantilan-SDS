using EEMOCantilanSDS.Application.Common.Interface.Time;
using System.Globalization;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;

/// <summary>
/// Assembles the admin Financial Reports payload by composing the canonical per-facility report
/// aggregation (stall facilities: NPM/TCC/NCC/BBQ/ICE) with the transaction facilities
/// (SLH per-head, TRM per-trip, TPM weekly market — all paid on service). No new aggregation is
/// introduced; figures reconcile to the same sources used by the Month-End report. Delinquent
/// (3+ unpaid months) and arrears (1–2 unpaid months) are split from stall compliance.
/// </summary>
public class GetFinancialReportQueryHandler(
    IFacilityReportsRepository reportsRepository,
    ISlaughterRepository slaughterRepository,
    ITrmRepository trmRepository,
    ITpmRepository tpmRepository,
    ITransactionFeedRepository transactionFeedRepository,
    IFacilityRepository facilityRepository,
    IClosedStallAccountQueries closedRegister,
    IFeeRateResolver feeRateResolver,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions,
    IClock clock
) : IRequestHandler<GetFinancialReportQuery, Result<FinancialReportDto>>
{
    private static readonly FacilityCode[] StallFacilities =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE,
          FacilityCode.Custom1, FacilityCode.Custom2, FacilityCode.Custom3, FacilityCode.Custom4, FacilityCode.Custom5 };

    // Paid-on-service facilities: collected at the point of service, so no recurring unpaid balance.
    private static readonly FacilityCode[] ServiceFacilities =
        { FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM };

    private const int AttentionLimit = 50;
    private const int RecentLimit = 8;

    public async Task<Result<FinancialReportDto>> Handle(GetFinancialReportQuery request, CancellationToken ct)
    {
        // "All time" aggregates every year into one view. Cached under a distinct key (year = 0 sentinel),
        // invalidated alongside the current period so new activity refreshes it.
        if (request.AllTime)
        {
            var allTimeKey = EemoCacheKeys.FinancialReport(tenantContext.TenantCode, ReportPeriod.Yearly, 0, null, request.Facility);
            var allTimeRegions = EemoCacheRegions.FinancialReportRegions(
                tenantContext.TenantCode, ReportPeriod.Yearly, clock.PhilippineToday.Year, null, request.Facility);
            var allTimeReport = await cache.GetOrCreateAsync(
                allTimeKey, allTimeRegions, cacheOptions.FinancialReportTtl,
                token => BuildAllTimeAsync(request, token), ct);
            return Result<FinancialReportDto>.Success(allTimeReport);
        }

        var normalizedRequest = NormalizeRequest(request, clock.PhilippineToday);
        var key = EemoCacheKeys.FinancialReport(
            tenantContext.TenantCode,
            normalizedRequest.Period,
            normalizedRequest.Year,
            normalizedRequest.Month,
            normalizedRequest.Facility);
        var regions = EemoCacheRegions.FinancialReportRegions(
            tenantContext.TenantCode,
            normalizedRequest.Period,
            normalizedRequest.Year,
            normalizedRequest.Month,
            normalizedRequest.Facility);

        var report = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.FinancialReportTtl,
            token => BuildFinancialReportAsync(normalizedRequest, token),
            ct);

        return Result<FinancialReportDto>.Success(report);
    }

    private async Task<FinancialReportDto> BuildFinancialReportAsync(GetFinancialReportQuery request, CancellationToken ct)
    {
        // Only the facilities the current tenant actually operates are reported. Cantilan has all eight
        // seeded, so its report is unchanged; other LGUs see only their configured facilities (no phantom
        // zero rows). Combined with the request.Facility scope filter below.
        var facilityNames = await facilityRepository.GetFacilityNamesAsync(ct);
        var tenantCodes = facilityNames.Keys.ToHashSet();
        bool InScope(FacilityCode c) => (request.Facility is null || request.Facility == c) && tenantCodes.Contains(c);

        // Resolve the municipality's NPM rates as of the report period (falls back to the ordinance
        // constants, so Cantilan figures are unchanged). Full-month reference = daily × 30.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var asOf = new DateOnly(request.Year, request.Month ?? 12, DateTime.DaysInMonth(request.Year, request.Month ?? 12));
        var npmDaily = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        var npmFish = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);
        // The office's own stated market month, or 0 where it states none and a month is thirty installments.
        var npmMonthlyStated = rateSnapshot.Resolve(FeeRateKey.NpmMonthlyStall, asOf);

        var facilityRows = new List<FinancialFacilityRowDto>();
        var stallTrend = new Dictionary<string, (decimal Collected, decimal Unpaid)>();

        decimal collected = 0m, unpaid = 0m;
        int paidRecords = 0, expectedRecords = 0;

        // ── Stall facilities — full canonical report (collected, unpaid, compliance, trend) ──
        foreach (var code in StallFacilities.Where(InScope))
        {
            var report = await reportsRepository.GetFacilityReportsAsync(
                code, request.Period, request.Year, request.Month, null, ct);

            // Electricity and water are the market's revenue too, so NPM's Collected states them alongside the stall
            // fees rather than beside them. They are held on utility bills, which no stall-fee path touches, so there
            // is nothing to double count: a utility payment writes only to its own bill.
            //
            // Only for a period a monthly bill can honestly be attributed to. A bill is billed for a MONTH, so a
            // weekly report cannot say which week its money belongs to, and folding a whole month into one week
            // would overstate that week. Weekly therefore counts stall fees alone, as it always did.
            var (utilElec, utilWater, utilOutstanding) = code == FacilityCode.NPM && request.Period != ReportPeriod.Weekly
                ? await NpmUtilitiesAsync(request.Year, request.Month, ct)
                : (0m, 0m, 0m);
            var utilCollected = utilElec + utilWater;

            collected += report.TotalRevenue + utilCollected;
            unpaid += report.PendingPaymentAmount + utilOutstanding;

            // "Paid records" counts actual collection transactions. NPM is collected per day, so the count comes
            // from the collections themselves — the repository counts them where each stall's own daily fee is
            // known, rather than dividing the period's money by one rate (which mis-counted a custom section and
            // any month carrying a month-end adjustment). Monthly-billed facilities keep one record per occupied
            // stall (counted when paid or partially paid).
            int paid, expected;
            if (code == FacilityCode.NPM)
            {
                paid = report.FeeTypeBreakdown?.PaidDayRecords ?? 0;
                expected = report.FeeTypeBreakdown?.ExpectedDayRecords ?? 0;
            }
            else
            {
                paid = report.CollectionPerformance.FullyPaidCount + report.CollectionPerformance.PartiallyPaidCount;
                expected = report.StallCompliance.Count;
            }
            paidRecords += paid;
            expectedRecords += expected;

            // NPM-only extras (fish split + full-month ₱900 coverage) for the expandable detail row.
            NpmFacilityDetailDto? detail = null;
            if (code == FacilityCode.NPM)
            {
                var dailyFee = report.FeeTypeBreakdown?.DailyFeeAmount ?? 0m;
                var fishFee = report.FeeTypeBreakdown?.FishFeeAmount ?? 0m;
                var fishKilos = npmFish > 0m ? fishFee / npmFish : 0m;
                // Mirror the Month-End report exactly: full-month coverage and its balance are summed
                // PER STALL — each NPM stall's fixed 30-day ₱900 reference, and max(0, ₱900 − that stall's
                // amount paid) — so the Financial and Month-End reports reconcile stall-for-stall. Coverage
                // is a monthly concept, so it is only produced for the Monthly period (0 = hidden otherwise).
                var coverage = 0m;
                var coverageBalance = 0m;
                var excusedAmount = 0m;
                if (request.Period == ReportPeriod.Monthly)
                {
                    // Mirror the Month-End report, stall for stall: the month each space is let for LESS its excused
                    // days, and the balance against that adjusted reference. Measured at the fee EACH stall is billed
                    // at, which the report carries per stall, so the two reports still reconcile where an office prices
                    // the areas of its market apart.
                    decimal StallDaily(StallComplianceDto s) => s.DailyRate > 0 ? s.DailyRate : npmDaily;
                    decimal Coverage(StallComplianceDto s) =>
                        rateSnapshot.MonthRule.Coverage(StallDaily(s), npmMonthlyStated, asOf.Day, s.AbsentDays);

                    coverage = report.StallCompliance.Sum(Coverage);
                    coverageBalance = report.StallCompliance.Sum(s => Math.Max(0m, Coverage(s) - s.AmountPaid));
                    excusedAmount = report.StallCompliance.Sum(s => s.AbsentDays * StallDaily(s));
                }
                // Electricity + water for the period, the same figures now counted in this row's Collected.
                detail = new NpmFacilityDetailDto(
                    DailyFeeCollected: dailyFee,
                    FishCollected: fishFee,
                    FishKilos: fishKilos,
                    PeriodBalance: report.PendingPaymentAmount,
                    FullMonthCoverage: coverage,
                    FullMonthCoverageBalance: coverageBalance,
                    ExcusedAmount: excusedAmount,
                    ElecCollected: utilElec,
                    WaterCollected: utilWater,
                    UtilityOutstanding: utilOutstanding);
            }

            // The row's own two figures decide its rate whenever utilities are part of them, so the percentage cannot
            // contradict the numbers beside it. With no utilities in play the repository's own rate is kept exactly,
            // which is every other facility and every NPM period without a utility bill.
            var rowCollected = report.TotalRevenue + utilCollected;
            var rowUnpaid = report.PendingPaymentAmount + utilOutstanding;
            var rowBilled = rowCollected + rowUnpaid;
            var rowRate = utilCollected + utilOutstanding > 0m
                ? (rowBilled > 0m ? (int)Math.Round(rowCollected / rowBilled * 100m) : 0)
                : (int)Math.Round(report.CollectionRate);

            facilityRows.Add(new FinancialFacilityRowDto(
                Code: code,
                Name: ReportName(code, facilityNames),
                Model: FacilityModel(code),
                PaidOnService: false,
                Collected: rowCollected,
                Unpaid: rowUnpaid,
                PaidRecords: paid,
                RatePct: rowRate,
                Status: StallStatus(rowRate),
                Detail: detail));

            // RevenueTrend is computed server-side with the report; sum across stall facilities by period.
            foreach (var t in report.RevenueTrend)
            {
                var periodUnpaid = t.ExpectedRevenue > t.Revenue ? t.ExpectedRevenue - t.Revenue : 0m;
                var acc = stallTrend.GetValueOrDefault(t.PeriodLabel);
                stallTrend[t.PeriodLabel] = (acc.Collected + t.Revenue, acc.Unpaid + periodUnpaid);
            }
        }

        // ── Service facilities — collected from their own month records (no unpaid, paid on service) ──
        foreach (var code in ServiceFacilities.Where(InScope))
        {
            var (svcCollected, svcRecords) = await ServiceTotalsAsync(code, request.Year, request.Month, ct);

            collected += svcCollected;
            paidRecords += svcRecords;
            expectedRecords += svcRecords; // paid on service = fully collected

            facilityRows.Add(new FinancialFacilityRowDto(
                Code: code,
                Name: ReportName(code, facilityNames),
                Model: FacilityModel(code),
                PaidOnService: true,
                Collected: svcCollected,
                Unpaid: null,
                PaidRecords: svcRecords,
                RatePct: svcRecords > 0 ? 100 : (int?)null,
                Status: "Paid on service"));
        }

        var billed = collected + unpaid;
        var ratePct = billed > 0m ? (int)Math.Round(collected / billed * 100m) : 0;

        // ── All-facility trend ──
        // Selected period bar = the headline figures (so it reconciles to the Collected KPI). Earlier
        // periods come from the stall facilities' server-side RevenueTrend; for the Monthly view the
        // paid-on-service facilities are folded into each earlier month too (one cheap query per month).
        // Earlier-YEAR bars stay stall-only to avoid a 12-month × facility query fan-out on the yearly view.
        var trend = new List<ReportTrendPointDto>();
        foreach (var (label, py, pm, isSelected) in BuildTrendWindow(request, clock.PhilippineToday))
        {
            decimal periodCollected;
            decimal periodUnpaid;
            if (isSelected)
            {
                periodCollected = collected;
                periodUnpaid = unpaid;
            }
            else
            {
                var st = stallTrend.GetValueOrDefault(label);
                periodCollected = st.Collected;
                periodUnpaid = st.Unpaid;

                // The market's utilities for THAT period, not this one. Without it the selected bar would stand
                // higher than every bar before it purely because only the selected period counted electricity and
                // water, which reads as a jump in collection that never happened.
                if (request.Period != ReportPeriod.Weekly && InScope(FacilityCode.NPM))
                {
                    var (elec, water, due) = await NpmUtilitiesAsync(py, pm, ct);
                    periodCollected += elec + water;
                    periodUnpaid += due;
                }

                if (request.Period == ReportPeriod.Monthly && pm is int pmonth)
                {
                    foreach (var svc in ServiceFacilities.Where(InScope))
                        periodCollected += (await ServiceMonthAsync(svc, py, pmonth, ct)).Collected;
                }
            }
            trend.Add(new ReportTrendPointDto(label, py, pm ?? 0, periodCollected, periodUnpaid, isSelected));
        }
        var ytdCollected = trend.Sum(t => t.Collected);

        // Month-over-month: the trend bar immediately before the selected one already holds the previous
        // period's all-facility collected (service is folded into prior monthly bars), so the delta is
        // accurate with no extra queries. Only meaningful for Monthly — yearly prior bars are stall-only.
        var selectedIdx = trend.FindIndex(t => t.IsSelected);
        decimal? collectedPreviousPeriod = request.Period == ReportPeriod.Monthly && selectedIdx > 0
            ? trend[selectedIdx - 1].Collected
            : null;

        // Attention & follow-up: the same delinquency source the dashboard and the queue read, stating each account's
        // whole position, classified by unpaid months.
        //
        // The anchor is the month the figures stop BEFORE, and it must come from the report's own period, not from
        // today. A Yearly report is anchored at January of the following year so it covers that whole year — asking
        // for 2024 used to borrow today's month and end on 31 July 2024, silently dropping August to December from a
        // printed report. The repository still clamps the anchor to the last month that has closed, so the current
        // year remains year-to-date rather than a projection.
        var (anchorYear, anchorMonth) = request.Month is { } m
            ? (request.Year, m)
            : (request.Year + 1, 1);
        var delinquency = await reportsRepository.GetDelinquentStallsAsync(request.Facility, anchorYear, anchorMonth, includeClosed: true, wholeAccount: true, ct);

        // Split by how many months are unpaid, then CAPPED for display. The cap keeps the payload bounded; the list is
        // ordered most-overdue first, so what survives it is the part the office would work through first.
        var delinquentAll = delinquency.Where(d => d.MonthsUnpaid >= 3).ToList();
        var arrearsAll = delinquency.Where(d => d.MonthsUnpaid is >= 1 and <= 2).ToList();

        var delinquent = delinquentAll
            .Take(AttentionLimit)
            .Select(ToAttention)
            .ToList();

        var arrears = arrearsAll
            .Take(AttentionLimit)
            .Select(ToAttention)
            .ToList();

        var feed = await transactionFeedRepository.GetRecentTransactionsAsync(request.Facility, null, RecentLimit, ct);
        var recent = feed.Select(f => new FinancialRecordDto(
            Reference: string.IsNullOrWhiteSpace(f.ORNumber) ? "—" : f.ORNumber!,
            Payor: f.Party,
            FacilityCode: f.FacilityCode,
            StallNo: f.Reference,
            RecordedAt: f.OccurredAt,
            Collector: null,
            Method: f.Kind,
            Amount: f.Amount)).ToList();

        var orderedRows = facilityRows.OrderBy(r => r.Code).ToList();
        var facilityCount = request.Facility is null
            ? StallFacilities.Concat(ServiceFacilities).Count(tenantCodes.Contains)
            : 1;

        // Accounts that have genuinely ENDED and still carry a balance — closed by an admin, or handed over to the
        // next lessee. Facility-scoped to match the report; all-time, because such a balance is not period-bound.
        //
        // A LAPSED account is deliberately excluded here. Its term ran out but the space was never handed over, so
        // the tenant is ordinarily still trading and the office keeps collecting: it is already counted in the
        // arrears and delinquency figures above. Counting it here as well stated the same debt twice — Cantilan
        // read "84 accounts need follow-up · ₱519,880" beside "closed / expired accounts ₱1,905,300", and 57 of
        // those 58 accounts were the same live receivables, over a longer span, presented as a separate sum.
        var closedAccounts = await closedRegister.GetClosedStallAccountsAsync(ct);
        var closedWithBalance = closedAccounts
            .Where(a => a.Uncollected > 0m
                && a.State != InactiveAccountState.Lapsed
                && (request.Facility is null || a.FacilityCode == request.Facility))
            .ToList();

        var dto = new FinancialReportDto(
            PeriodLabel: PeriodLabel(request),
            ScopeLabel: request.Facility is null ? "All facilities" : ReportName(request.Facility.Value, facilityNames),
            Frequency: FrequencyLabel(request.Period),
            FacilityCount: facilityCount,
            Collected: collected,
            CurrentPeriodUnpaid: unpaid,
            Billed: billed,
            CollectionRatePct: ratePct,
            PaidRecords: paidRecords,
            ExpectedRecords: expectedRecords,
            CollectedPreviousPeriod: collectedPreviousPeriod,
            PreviousPeriodLabel: PreviousPeriodLabel(request),
            Delinquent: delinquent,
            Arrears: arrears,
            Trend: trend,
            YtdCollected: ytdCollected,
            Facilities: orderedRows,
            RecentRecords: recent,
            ClosedWithBalanceCount: closedWithBalance.Count,
            ClosedWithBalanceOutstanding: closedWithBalance.Sum(a => a.Uncollected),
            // The last month of THIS report's period that has closed — the same boundary the delinquency source used,
            // so the page states the span it was actually given rather than naming today's month.
            AttentionSpanLabel: AttentionSpanLabel(anchorYear, anchorMonth, clock.PhilippineToday),
            // Counted over every account, not over the capped lists above. The header states these.
            DelinquentAccountsTotal: delinquentAll.Count,
            DelinquentOutstandingTotal: delinquentAll.Sum(d => d.OutstandingBalance),
            ArrearsAccountsTotal: arrearsAll.Count,
            ArrearsOutstandingTotal: arrearsAll.Sum(d => d.OutstandingBalance));

        return dto;
    }

    // "All time": aggregate every year of data (system epoch → now) into one view. Reuses the proven
    // per-year build and merges the results — no new aggregation math. Scalar KPIs sum across years, the
    // trend shows one bar per active year, per-facility rows are summed, and delinquency uses the current
    // rolling window (the latest year's). Cantilan/tests are unaffected (this path only runs when AllTime).
    private async Task<FinancialReportDto> BuildAllTimeAsync(GetFinancialReportQuery request, CancellationToken ct)
    {
        const int epochYear = 2020;   // system data epoch — no transactions predate this
        var currentYear = clock.PhilippineToday.Year;

        // Start where the tenant's records actually start. Building every year back to the epoch meant a whole
        // report per year — each one walking every facility — for years in which the office had no data at all.
        // Three cheap MIN probes replace them. A tenant that went live this year now builds one year, not seven.
        var earliest = Math.Max(epochYear, Math.Min(currentYear, await reportsRepository.GetEarliestActivityYearAsync(ct)));

        var yearly = new List<(int Year, FinancialReportDto Dto)>();
        for (var y = earliest; y <= currentYear; y++)
        {
            var dto = await BuildFinancialReportAsync(
                request with { AllTime = false, Period = ReportPeriod.Yearly, Year = y, Month = null }, ct);
            yearly.Add((y, dto));
        }

        var collected = yearly.Sum(x => x.Dto.Collected);
        var unpaid = yearly.Sum(x => x.Dto.CurrentPeriodUnpaid);
        var billed = collected + unpaid;
        var ratePct = billed > 0m ? (int)Math.Round(collected / billed * 100m) : 0;
        var paidRecords = yearly.Sum(x => x.Dto.PaidRecords);
        var expectedRecords = yearly.Sum(x => x.Dto.ExpectedRecords);

        // One trend bar per year that had activity; the most recent year is the highlighted bar.
        var activeYears = yearly.Where(x => x.Dto.Collected > 0m || x.Dto.CurrentPeriodUnpaid > 0m).ToList();
        if (activeYears.Count == 0)
            activeYears = yearly.Where(x => x.Year == currentYear).ToList();
        var trend = activeYears
            .Select(x => new ReportTrendPointDto(
                x.Year.ToString(), x.Year, 0, x.Dto.Collected, x.Dto.CurrentPeriodUnpaid, x.Year == currentYear))
            .ToList();

        // Merge per-facility rows across years (sum money + records; recompute rate/status).
        var facilities = yearly
            .SelectMany(x => x.Dto.Facilities)
            .GroupBy(f => f.Code)
            .Select(g =>
            {
                var first = g.First();
                var fCollected = g.Sum(f => f.Collected);
                var fUnpaid = g.Sum(f => f.Unpaid ?? 0m);
                var fBilled = fCollected + fUnpaid;
                int? fRate = first.PaidOnService
                    ? (g.Sum(f => f.PaidRecords) > 0 ? 100 : (int?)null)
                    : (fBilled > 0m ? (int)Math.Round(fCollected / fBilled * 100m) : 0);
                return new FinancialFacilityRowDto(
                    Code: first.Code,
                    Name: first.Name,
                    Model: first.Model,
                    PaidOnService: first.PaidOnService,
                    Collected: fCollected,
                    Unpaid: first.PaidOnService ? null : fUnpaid,
                    PaidRecords: g.Sum(f => f.PaidRecords),
                    RatePct: fRate,
                    Status: first.PaidOnService ? "Paid on service" : StallStatus(fRate ?? 0),
                    Detail: null);
            })
            .OrderBy(r => r.Code)
            .ToList();

        var latest = yearly.First(x => x.Year == currentYear).Dto;

        return new FinancialReportDto(
            PeriodLabel: "All time",
            ScopeLabel: latest.ScopeLabel,
            Frequency: "All time",
            FacilityCount: latest.FacilityCount,
            Collected: collected,
            CurrentPeriodUnpaid: unpaid,
            Billed: billed,
            CollectionRatePct: ratePct,
            PaidRecords: paidRecords,
            ExpectedRecords: expectedRecords,
            CollectedPreviousPeriod: null,
            PreviousPeriodLabel: null,
            Delinquent: latest.Delinquent,
            Arrears: latest.Arrears,
            Trend: trend,
            YtdCollected: collected,
            Facilities: facilities,
            RecentRecords: latest.RecentRecords,
            ClosedWithBalanceCount: latest.ClosedWithBalanceCount,
            ClosedWithBalanceOutstanding: latest.ClosedWithBalanceOutstanding,
            // Carried across with the lists they describe. Delinquency here is the CURRENT position (see the note above),
            // so its totals are the current year's — dropping them would have left the header at nought accounts and no
            // money owed while the lists beneath it showed both.
            AttentionSpanLabel: latest.AttentionSpanLabel,
            DelinquentAccountsTotal: latest.DelinquentAccountsTotal,
            DelinquentOutstandingTotal: latest.DelinquentOutstandingTotal,
            ArrearsAccountsTotal: latest.ArrearsAccountsTotal,
            ArrearsOutstandingTotal: latest.ArrearsOutstandingTotal);
    }

    /// <param name="today">
    /// Passed in rather than read here. A static helper that reaches for a clock cannot be tested, and this one decides
    /// which month an unqualified monthly report means — the difference between "this month" and a fixed month.
    /// </param>
    private static GetFinancialReportQuery NormalizeRequest(GetFinancialReportQuery request, DateOnly today)
        => request.Period == ReportPeriod.Monthly && request.Month is null
            ? request with { Month = today.Month }
            : request;

    /// <summary>
    /// The month the attention figures are counted up to: the month before the anchor, or the last month that has
    /// closed if the anchor is still ahead of it. Mirrors the clamp the delinquency source applies, so the page and
    /// the figures cannot describe different spans.
    /// </summary>
    private static string AttentionSpanLabel(int anchorYear, int anchorMonth, DateOnly today)
    {
        var end = new DateOnly(anchorYear, anchorMonth, 1).AddMonths(-1);
        var lastClosed = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        if (end > lastClosed) end = lastClosed;
        return end.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    private static AttentionAccountDto ToAttention(DelinquentStallDto d) => new(
        Name: string.IsNullOrWhiteSpace(d.Occupant) ? "Unoccupied / unnamed" : d.Occupant,
        FacilityCode: d.FacilityCode,
        StallNo: d.StallNo,
        Location: string.IsNullOrWhiteSpace(d.Section)
            // Parts that are absent are dropped rather than interpolated: a space the office does not number
            // contributes no stall part, and would otherwise leave a trailing separator after the facility.
            ? JoinParts(d.FacilityCode.ToString(), SpaceNumber.Describe(d.StallNo))
            // The market numbers per section, so three different payors would all read "NPM · Stall 1".
            : JoinParts(d.FacilityCode.ToString(), d.Section, SpaceNumber.Describe(d.StallNo)),
        Balance: d.OutstandingBalance,
        UnpaidMonths: d.MonthsUnpaid,
        TermLapsed: d.TermLapsed,
        // Carried so the row can link to the stall itself. A facility-and-number link opened whichever of the
        // market's three "Stall 1" spaces the lookup happened to find first.
        StallId: d.StallId);

    /// <summary>
    /// The trend window, matching the repo's RevenueTrend: Monthly = last 6 months (label "MMM yyyy"),
    /// Yearly = last 5 years (label "yyyy"), both ending at the selected period (flagged IsSelected).
    /// </summary>
    private static IReadOnlyList<(string Label, int Year, int? Month, bool IsSelected)> BuildTrendWindow(GetFinancialReportQuery request, DateOnly today)
    {
        var window = new List<(string, int, int?, bool)>();
        if (request.Period == ReportPeriod.Yearly)
        {
            for (var i = 4; i >= 0; i--)
            {
                var y = request.Year - i;
                window.Add((y.ToString(), y, null, i == 0));
            }
        }
        else
        {
            var anchorMonth = request.Month ?? today.Month;
            for (var i = 5; i >= 0; i--)
            {
                var d = new DateTime(request.Year, anchorMonth, 1).AddMonths(-i);
                window.Add((d.ToString("MMM yyyy"), d.Year, d.Month, i == 0));
            }
        }
        return window;
    }

    /// <summary>
    /// The market's electricity and water for a period: what was paid, split by utility, and what is still due.
    ///
    /// <para>
    /// One place, called for the row and for every bar of the trend, so the table and the chart cannot come to
    /// different answers. A null month means the whole year, which is how the underlying query already reads it.
    /// </para>
    /// </summary>
    private async Task<(decimal Elec, decimal Water, decimal Outstanding)> NpmUtilitiesAsync(int year, int? month, CancellationToken ct)
    {
        var util = await reportsRepository.GetNpmUtilityTotalsAsync(year, month, ct);
        return (util.ElecCollected, util.WaterCollected, util.Outstanding);
    }

    /// <summary>Collected total and record count for a paid-on-service facility, for one month or a full year.</summary>
    private async Task<(decimal Collected, int Records)> ServiceTotalsAsync(FacilityCode code, int year, int? month, CancellationToken ct)
    {
        if (month is int m)
            return await ServiceMonthAsync(code, year, m, ct);

        decimal collected = 0m;
        int records = 0;
        for (var mm = 1; mm <= 12; mm++)
        {
            var (c, r) = await ServiceMonthAsync(code, year, mm, ct);
            collected += c;
            records += r;
        }
        return (collected, records);
    }

    private async Task<(decimal Collected, int Records)> ServiceMonthAsync(FacilityCode code, int year, int month, CancellationToken ct)
    {
        switch (code)
        {
            case FacilityCode.SLH:
            {
                var rows = await slaughterRepository.GetTransactionsByMonthAsync(year, month, ct);
                return (rows.Sum(t => t.TotalAmount), rows.Count);
            }
            case FacilityCode.TRM:
            {
                var trips = await trmRepository.GetTripsByMonthAsync(year, month, ct);
                return (trips.Sum(t => t.Fee), trips.Count);
            }
            case FacilityCode.TPM:
            {
                var attendance = await tpmRepository.GetMonthAttendanceAsync(year, month, ct);
                var paid = attendance.Where(a => a.IsPaid).ToList();
                return (paid.Sum(a => a.Fee), paid.Count);
            }
            default:
                return (0m, 0);
        }
    }

    private static string PeriodLabel(GetFinancialReportQuery r) => r.Period switch
    {
        ReportPeriod.Monthly when r.Month is int m => $"{MonthName(m)} {r.Year}",
        ReportPeriod.Yearly => r.Year.ToString(),
        _ => r.Year.ToString()
    };

    private static string? PreviousPeriodLabel(GetFinancialReportQuery r)
    {
        if (r.Period == ReportPeriod.Monthly && r.Month is int m)
        {
            var prev = new DateTime(r.Year, m, 1).AddMonths(-1);
            return $"{MonthName(prev.Month)} {prev.Year}";
        }
        return r.Period == ReportPeriod.Yearly ? (r.Year - 1).ToString() : null;
    }

    private static string FrequencyLabel(ReportPeriod p) => p switch
    {
        ReportPeriod.Monthly => "Monthly",
        ReportPeriod.Yearly => "Annual",
        ReportPeriod.Weekly => "Weekly",
        _ => p.ToString()
    };

    private static string MonthName(int month) =>
        new DateTime(2000, month, 1).ToString("MMMM", CultureInfo.InvariantCulture);

    private static string StallStatus(int ratePct) => ratePct switch
    {
        >= 85 => "Good",
        >= 70 => "On track",
        _ => "Behind"
    };

    private static string FacilityModel(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "Daily stall",
        FacilityCode.TCC or FacilityCode.NCC or FacilityCode.BBQ or FacilityCode.ICE => "Monthly rental",
        FacilityCode.SLH => "Per-head",
        FacilityCode.TRM => "Per-trip",
        FacilityCode.TPM => "Weekly market",
        _ when FacilityCatalog.IsCustom(code) => "Monthly rental",
        _ => "—"
    };

    private static string FacilityName(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "New Public Market",
        FacilityCode.TCC => "Tampak Commercial Center",
        FacilityCode.NCC => "New Commercial Center",
        FacilityCode.BBQ => "Barbecue Stand",
        FacilityCode.ICE => "Iceplant",
        FacilityCode.SLH => "Slaughterhouse",
        FacilityCode.TRM => "Transport Terminal",
        FacilityCode.TPM => "Tabo-an Public Market",
        _ => code.ToString()
    };

    // Head-named custom facilities use their stored name; canonical facilities keep the fixed label.
    private static string ReportName(FacilityCode code, IReadOnlyDictionary<FacilityCode, string> names) =>
        names.TryGetValue(code, out var n) && !string.IsNullOrWhiteSpace(n) ? n : FacilityName(code);

    /// <summary>
    /// Joins the parts of an account's location, dropping any that are absent. The stall part is legitimately empty
    /// for a space the office does not number, and interpolating it left a trailing separator after the facility.
    /// </summary>
    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
