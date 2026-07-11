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
    IFeeRateResolver feeRateResolver,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions
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
        var normalizedRequest = NormalizeRequest(request);
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
        var npmMonthly = npmDaily * 30m;

        var facilityRows = new List<FinancialFacilityRowDto>();
        var stallTrend = new Dictionary<string, (decimal Collected, decimal Unpaid)>();

        decimal collected = 0m, unpaid = 0m;
        int paidRecords = 0, expectedRecords = 0;

        // ── Stall facilities — full canonical report (collected, unpaid, compliance, trend) ──
        foreach (var code in StallFacilities.Where(InScope))
        {
            var report = await reportsRepository.GetFacilityReportsAsync(
                code, request.Period, request.Year, request.Month, null, ct);

            collected += report.TotalRevenue;
            unpaid += report.PendingPaymentAmount;

            // "Paid records" counts actual collection transactions. NPM is collected per day, so each
            // ₱30 daily collection is one record, and "expected" is the collectable stall-days (the daily
            // obligation ÷ ₱30, already absent-adjusted via ExpectedBill). Monthly-billed facilities keep
            // one record per occupied stall (counted when paid or partially paid).
            int paid, expected;
            if (code == FacilityCode.NPM)
            {
                paid = (int)Math.Round((report.FeeTypeBreakdown?.DailyFeeAmount ?? 0m) / npmDaily);
                expected = (int)Math.Round(report.StallCompliance.Sum(s => s.ExpectedBill) / npmDaily);
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
                    // Mirror the Month-End report: each NPM stall's ₱900 full-month reference LESS its
                    // excused/absent days (₱30/day), and the balance against that absent-adjusted
                    // reference — so the Financial and Month-End reports reconcile stall-for-stall.
                    coverage = report.StallCompliance.Sum(s => Math.Max(0m, npmMonthly - s.AbsentDays * npmDaily));
                    coverageBalance = report.StallCompliance.Sum(s =>
                        Math.Max(0m, Math.Max(0m, npmMonthly - s.AbsentDays * npmDaily) - s.AmountPaid));
                    excusedAmount = report.StallCompliance.Sum(s => s.AbsentDays * npmDaily);
                }
                detail = new NpmFacilityDetailDto(
                    DailyFeeCollected: dailyFee,
                    FishCollected: fishFee,
                    FishKilos: fishKilos,
                    PeriodBalance: report.PendingPaymentAmount,
                    FullMonthCoverage: coverage,
                    FullMonthCoverageBalance: coverageBalance,
                    ExcusedAmount: excusedAmount);
            }

            facilityRows.Add(new FinancialFacilityRowDto(
                Code: code,
                Name: ReportName(code, facilityNames),
                Model: FacilityModel(code),
                PaidOnService: false,
                Collected: report.TotalRevenue,
                Unpaid: report.PendingPaymentAmount,
                PaidRecords: paid,
                RatePct: (int)Math.Round(report.CollectionRate),
                Status: StallStatus((int)Math.Round(report.CollectionRate)),
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
                Name: FacilityName(code),
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
        foreach (var (label, py, pm, isSelected) in BuildTrendWindow(request))
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

        // Attention & follow-up: shared rolling-window delinquency (cumulative balance, excludes the
        // current month), classified by unpaid months — identical source to the dashboard.
        var anchorMonth = request.Month ?? PhilippineTime.Today.Month;
        var delinquency = await reportsRepository.GetDelinquentStallsAsync(request.Facility, request.Year, anchorMonth, includeClosed: true, ct);

        var delinquent = delinquency
            .Where(d => d.MonthsUnpaid >= 3)
            .Take(AttentionLimit)
            .Select(ToAttention)
            .ToList();

        var arrears = delinquency
            .Where(d => d.MonthsUnpaid is >= 1 and <= 2)
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

        var dto = new FinancialReportDto(
            PeriodLabel: PeriodLabel(request),
            ScopeLabel: request.Facility is null ? "All facilities" : FacilityName(request.Facility.Value),
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
            RecentRecords: recent);

        return dto;
    }

    private static GetFinancialReportQuery NormalizeRequest(GetFinancialReportQuery request)
        => request.Period == ReportPeriod.Monthly && request.Month is null
            ? request with { Month = PhilippineTime.Today.Month }
            : request;

    private static AttentionAccountDto ToAttention(DelinquentStallDto d) => new(
        Name: string.IsNullOrWhiteSpace(d.Occupant) ? "Unoccupied / unnamed" : d.Occupant,
        FacilityCode: d.FacilityCode,
        StallNo: d.StallNo,
        Location: $"{d.FacilityCode} · Stall {d.StallNo}",
        Balance: d.OutstandingBalance,
        UnpaidMonths: d.MonthsUnpaid);

    /// <summary>
    /// The trend window, matching the repo's RevenueTrend: Monthly = last 6 months (label "MMM yyyy"),
    /// Yearly = last 5 years (label "yyyy"), both ending at the selected period (flagged IsSelected).
    /// </summary>
    private static IReadOnlyList<(string Label, int Year, int? Month, bool IsSelected)> BuildTrendWindow(GetFinancialReportQuery request)
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
            var anchorMonth = request.Month ?? PhilippineTime.Today.Month;
            for (var i = 5; i >= 0; i--)
            {
                var d = new DateTime(request.Year, anchorMonth, 1).AddMonths(-i);
                window.Add((d.ToString("MMM yyyy"), d.Year, d.Month, i == 0));
            }
        }
        return window;
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
}
