using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistory;

/// <summary>
/// Builds the Follow-up History snapshot for a past period. Identical composition to the live queue
/// (via <see cref="FollowUpComposer"/>), except:
///   • contract attention is evaluated as of the END of the requested period (not "today");
///   • online awaiting-OR is scoped to that billing period;
///   • the snapshot date stamped on the DTO is the last day of the period.
/// The remaining sources (delinquency rolling window, per-facility compliance, service-facility and
/// cash missing-OR) are already period-parameterised, so they need no change.
/// </summary>
public class GetFollowUpHistoryQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IStallRepository stallRepository,
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    ISlaughterRepository slaughterRepository,
    ITrmRepository trmRepository,
    ITpmRepository tpmRepository,
    IUtilityBillRepository utilityBillRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions
) : IRequestHandler<GetFollowUpHistoryQuery, Result<FollowUpQueueDto>>
{
    public async Task<Result<FollowUpQueueDto>> Handle(GetFollowUpHistoryQuery request, CancellationToken ct)
    {
        var key = request.AllTime
            // A cumulative view: it does not belong to a year or a month, so it gets its own key. Otherwise it
            // would collide with (and serve) the selected year's snapshot.
            ? EemoCacheKeys.FollowUpHistoryAllTime(tenantContext.TenantCode)
            : EemoCacheKeys.FollowUpHistory(tenantContext.TenantCode, request.Year, request.Month, request.WholeYear);
        var regions = request.AllTime
            ? EemoCacheRegions.FollowUpAllTimeRegions(tenantContext.TenantCode)
            : EemoCacheRegions.FollowUpHistoryRegions(tenantContext.TenantCode, request.Year, request.Month, request.WholeYear);
        var history = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.FollowUpHistoryTtl,
            token => BuildHistoryAsync(request, token),
            ct);

        return Result<FollowUpQueueDto>.Success(history);
    }

    private async Task<FollowUpQueueDto> BuildHistoryAsync(GetFollowUpHistoryQuery request, CancellationToken ct)
    {
        var year = request.Year;
        var month = request.Month;
        var asOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        // ── Whole time ────────────────────────────────────────────────────────────────────────────────────
        // "What is still owed, by whom, in total" — the question the office cannot answer by paging through one
        // year at a time. Only the sources that are inherently cumulative are used: the inactive-account register
        // (each ended occupancy's whole balance) and contracts that have lapsed (their register total). The
        // period-only categories — a month's unpaid, that month's missing receipts, the rolling delinquency
        // window, a month's utility bills — are deliberately absent: they have no meaning across all time, and
        // showing a single month's figure under a "whole time" heading is exactly the contradiction this fixes.
        if (request.AllTime)
        {
            var allAccounts = await stallRepository.GetClosedStallAccountsAsync(ct);
            var allBalances = allAccounts
                .Where(a => a.Uncollected > 0m)
                .GroupBy(a => $"{a.FacilityCode}|{a.StallNo}")
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Uncollected));

            var lapsed = (await stallRepository.GetContractAttentionAsync(DomainRules.ExpiringSoonMonths, ct))
                .Where(c => c.IsExpired)
                .ToList();

            // The whole-time view used to pass no delinquency at all, so its Delinquent and Arrears chips read 0
            // while 58 accounts sat under "Contract expired" — a page telling the office there are no delinquents.
            // Safe to state now that one occupancy contributes one balance: the lapsed rows beside these keep their
            // renewal status and action but no longer restate the money. Asked for the WHOLE account, because that
            // is what a whole-time view means, and it agrees with the register beside it.
            var wholeAccountDelinquency = await reportsRepository.GetDelinquentStallsAsync(
                null, year, month, includeClosed: false, wholeAccount: true, ct);

            return FollowUpComposer.Compose(
                year, month, PhilippineTime.Today,
                wholeAccountDelinquency,
                new Dictionary<FacilityCode, FacilityReportsDto>(),
                Array.Empty<OnlinePaymentAwaitingOrDto>(),
                Array.Empty<SlaughterTransactionDto>(),
                Array.Empty<TrmTripDto>(),
                Array.Empty<TpmVendorAttendanceDto>(),
                Array.Empty<UnreceiptedPaymentDto>(),
                lapsed,
                Array.Empty<UtilityBill>(),
                allBalances,
                allAccounts,
                periodLabelOverride: "Whole time",
                // These figures are each account's entire position, so the rows say so rather than borrowing the
                // page's heading.
                delinquencySpanLabel: "Whole account");
        }

        var facilityReports = new Dictionary<FacilityCode, FacilityReportsDto>();
        foreach (var code in FollowUpComposer.StallFacilities)
        {
            // "Whole year" must mean January to December, not the year's last month. Asking the reports for the
            // YEARLY period widens every stall's assessment to the whole year in one pass — one row per account
            // carrying the year's obligation and the year's balance — instead of showing December's figure under a
            // whole-year heading. Deriving it month by month would rebuild every facility's report twelve times.
            facilityReports[code] = request.WholeYear
                ? await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Yearly, year, month, null, ct)
                : await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, ct);
        }

        var delinquency = await reportsRepository.GetDelinquentStallsAsync(null, year, month, ct);

        // Online payments still awaiting a receipt, and utility bills, are recorded per month and have no year-wide
        // read. Both are small, single-table lookups, so a whole-year view gathers the year's months rather than
        // showing one month's items under a whole-year heading.
        var awaitingOr = new List<OnlinePaymentAwaitingOrDto>();
        var utilityBills = new List<UtilityBill>();
        foreach (var m in MonthsOf(request, year, month))
        {
            awaitingOr.AddRange(await onlinePaymentRepository.GetAwaitingOrByPeriodAsync(year, m, ct));
            utilityBills.AddRange(await utilityBillRepository.GetForMonthAsync(year, m, ct));
        }
        var slaughter = request.WholeYear
            ? await slaughterRepository.GetTransactionsByYearAsync(year, ct)
            : await slaughterRepository.GetTransactionsByMonthAsync(year, month, ct);
        var trips = request.WholeYear
            ? await trmRepository.GetTripsByYearAsync(year, ct)
            : await trmRepository.GetTripsByMonthAsync(year, month, ct);
        var attendance = request.WholeYear
            ? await tpmRepository.GetYearAttendanceAsync(year, ct)
            : await tpmRepository.GetMonthAttendanceAsync(year, month, ct);
        // Missing-OR (cash/field) source: whole-year aggregates every month so a blank-OR settlement in
        // ANY month surfaces; a specific month keeps the exact single-month behaviour (unchanged).
        var unreceipted = request.WholeYear
            ? await paymentRepository.GetUnreceiptedCashPaymentsForYearAsync(year, ct)
            : await paymentRepository.GetUnreceiptedCashPaymentsAsync(year, month, ct);
        var contracts = await stallRepository.GetContractAttentionAsOfAsync(year, month, DomainRules.ExpiringSoonMonths, ct);

        // The window this snapshot is scoped to. A whole-year view runs from January; a single month is its own
        // window. The end never runs past the snapshot date, so a still-running occupancy is never stated into
        // the future. Every figure and every span on the rows below is bounded to it.
        var periodStart = request.WholeYear ? new DateOnly(year, 1, 1) : new DateOnly(year, month, 1);
        var periodEnd = asOf;

        // What each ended occupancy owed and paid FOR this period. The lifetime register answers "what is owed in
        // total" and belongs to "Whole time"; stating it here put a lifetime balance under a period heading.
        var closedAccounts = await stallRepository.GetClosedStallAccountsForPeriodAsync(periodStart, periodEnd, ct);

        var dto = FollowUpComposer.Compose(
            year, month, asOf,
            delinquency, facilityReports, awaitingOr,
            slaughter, trips, attendance, unreceipted, contracts, utilityBills,
            // A view scoped to a period states that period's figures. Lifetime balances belong to "Whole time",
            // which is the view that exists to answer "what is owed in total".
            expiredBalances: null,
            closedAccounts,
            // A whole-year view assesses January to December, so its rows must say so rather than naming the year's
            // last month — the figure beside them is the year's, not that month's.
            periodLabelOverride: request.WholeYear
                ? $"January – December {year}"
                : null,
            periodStart: periodStart,
            periodEnd: periodEnd,
            // A year or month view's delinquency figures are a rolling twelve months to the last closed month, not
            // that heading's span — Nora's row read "January – December 2026" beside a count of 37 months.
            delinquencySpanLabel: RollingYearLabel(year, month));

        return dto;
    }

    /// <summary>
    /// The months a snapshot covers: every month of the year up to the reference month for a whole-year view (a
    /// future month of the current year holds nothing), or just the one month asked for.
    /// </summary>
    private static IEnumerable<int> MonthsOf(GetFollowUpHistoryQuery request, int year, int month)
    {
        if (!request.WholeYear)
            return new[] { month };

        var last = year == PhilippineTime.Today.Year ? PhilippineTime.Today.Month : 12;
        return Enumerable.Range(1, Math.Max(1, last));
    }

    /// <summary>"12 months to July 2026" — the span a rolling delinquency figure covers, ending with the last month
    /// that closed. Stated on the row so a year heading is never read as the span of the money beside it.</summary>
    private static string RollingYearLabel(int year, int month)
    {
        var anchor = new DateOnly(year, month, 1);
        var today = PhilippineTime.Today;
        var lastElapsed = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var end = anchor.AddMonths(-1) < lastElapsed ? anchor.AddMonths(-1) : lastElapsed;
        return $"12 months to {end.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
