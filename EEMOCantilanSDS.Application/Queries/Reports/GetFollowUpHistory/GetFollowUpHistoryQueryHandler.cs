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

            return FollowUpComposer.Compose(
                year, month, PhilippineTime.Today,
                Array.Empty<DelinquentStallDto>(),
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
                periodLabelOverride: "Whole time");
        }

        var facilityReports = new Dictionary<FacilityCode, FacilityReportsDto>();
        foreach (var code in FollowUpComposer.StallFacilities)
            facilityReports[code] = await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, ct);

        var delinquency = await reportsRepository.GetDelinquentStallsAsync(null, year, month, ct);
        var awaitingOr = await onlinePaymentRepository.GetAwaitingOrByPeriodAsync(year, month, ct);
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
        var utilityBills = await utilityBillRepository.GetForMonthAsync(year, month, ct);

        // Full outstanding balance per expired/closed account (register total), so an expired follow-up
        // row shows its whole balance and (for monthly facilities) becomes payable via the shared modal.
        var closedAccounts = await stallRepository.GetClosedStallAccountsAsync(ct);
        var expiredBalances = closedAccounts
            .Where(a => a.Uncollected > 0m)
            .GroupBy(a => $"{a.FacilityCode}|{a.StallNo}")
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Uncollected));

        var dto = FollowUpComposer.Compose(
            year, month, asOf,
            delinquency, facilityReports, awaitingOr,
            slaughter, trips, attendance, unreceipted, contracts, utilityBills,
            expiredBalances,
            closedAccounts);

        return dto;
    }
}
