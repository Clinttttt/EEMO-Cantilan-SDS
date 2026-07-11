using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
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
        var key = EemoCacheKeys.FollowUpHistory(tenantContext.TenantCode, request.Year, request.Month);
        var regions = EemoCacheRegions.FollowUpHistoryRegions(tenantContext.TenantCode, request.Year, request.Month);
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

        var facilityReports = new Dictionary<FacilityCode, FacilityReportsDto>();
        foreach (var code in FollowUpComposer.StallFacilities)
            facilityReports[code] = await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, ct);

        var delinquency = await reportsRepository.GetDelinquentStallsAsync(null, year, month, ct);
        var awaitingOr = await onlinePaymentRepository.GetAwaitingOrByPeriodAsync(year, month, ct);
        var slaughter = await slaughterRepository.GetTransactionsByMonthAsync(year, month, ct);
        var trips = await trmRepository.GetTripsByMonthAsync(year, month, ct);
        var attendance = await tpmRepository.GetMonthAttendanceAsync(year, month, ct);
        var unreceipted = await paymentRepository.GetUnreceiptedCashPaymentsAsync(year, month, ct);
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
            expiredBalances);

        return dto;
    }
}
