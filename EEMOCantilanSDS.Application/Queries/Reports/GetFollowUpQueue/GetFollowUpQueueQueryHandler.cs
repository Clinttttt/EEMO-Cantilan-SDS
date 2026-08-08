using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;

/// <summary>
/// Assembles the admin Follow-up Queue "as of today" by fetching the canonical sources and handing them
/// to <see cref="FollowUpComposer"/>. The contract-attention and online-awaiting-OR sources use their
/// live ("current") variants here; the history handler swaps in period-scoped variants but shares the
/// same composer so every other rule stays identical.
/// </summary>
public class GetFollowUpQueueQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IStallRepository stallRepository,
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    ISlaughterRepository slaughterRepository,
    ITrmRepository trmRepository,
    ITpmRepository tpmRepository,
    IUtilityBillRepository utilityBillRepository
) : IRequestHandler<GetFollowUpQueueQuery, Result<FollowUpQueueDto>>
{
    public async Task<Result<FollowUpQueueDto>> Handle(GetFollowUpQueueQuery request, CancellationToken ct)
    {
        var year = request.Year;
        var month = request.Month;

        var facilityReports = new Dictionary<FacilityCode, FacilityReportsDto>();
        foreach (var code in FollowUpComposer.StallFacilities)
            facilityReports[code] = await reportsRepository.GetFacilityReportsAsync(code, ReportPeriod.Monthly, year, month, null, ct);

        var delinquency = await reportsRepository.GetDelinquentStallsAsync(null, year, month, ct);
        var awaitingOr = await onlinePaymentRepository.GetAwaitingOrAsync(ct);
        var slaughter = await slaughterRepository.GetTransactionsByMonthAsync(year, month, ct);
        var trips = await trmRepository.GetTripsByMonthAsync(year, month, ct);
        var attendance = await tpmRepository.GetMonthAttendanceAsync(year, month, ct);
        var unreceipted = await paymentRepository.GetUnreceiptedCashPaymentsAsync(year, month, ct);
        // Live queue shows only contracts EXPIRING SOON (still active). Already-EXPIRED contracts are a
        // past concern and belong on Past follow-up (the history handler keeps them via its as-of scope),
        // so they are excluded here to stop them counting/showing in the active queue.
        var contracts = (await stallRepository.GetContractAttentionAsync(DomainRules.ExpiringSoonMonths, ct))
            .Where(c => !c.IsExpired)
            .ToList();
        var utilityBills = await utilityBillRepository.GetForMonthAsync(year, month, ct);

        // ── Accounts that ended THIS period and still owe ──
        // An occupancy closed or handed over during the month the office is working is current follow-up work: the
        // lessee has gone but the money has not been collected, and this is the last period in which anyone will be
        // looking. The live queue was given no inactive accounts at all, so those balances appeared nowhere on it —
        // ₱1,500 owed by a lessee closed on the 8th was simply off the screen until somebody thought to open the
        // whole-time view.
        //
        // Only occupancies that ended inside the period, so the queue does not silently become the register: older
        // ended accounts stay where they are read, on the Whole-time History and the register of inactive accounts.
        // The whole remaining balance is stated, not the period's slice of it, because a closed account's balance is
        // final — nothing further will be billed and there is no later period for the remainder to surface in.
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = DomainRules.EarnedThrough(periodStart.AddMonths(1).AddDays(-1), PhilippineTime.Today);
        var endedThisPeriod = (await stallRepository.GetClosedStallAccountsAsync(ct))
            .Where(a => a.Uncollected > 0m)
            .Where(a =>
            {
                var ended = a.OccupancyEndedOn ?? a.ClosedOn ?? a.ExpiryDate;
                return ended >= periodStart && ended <= periodEnd;
            })
            .ToList();

        var dto = FollowUpComposer.Compose(
            year, month, PhilippineTime.Today,
            delinquency, facilityReports, awaitingOr,
            slaughter, trips, attendance, unreceipted, contracts, utilityBills,
            // This page is the collector's work for the month, and its delinquency figures are a rolling twelve
            // months to the last month that closed — not August's. The rows stay, because the collector must still
            // see who is behind; what changes is that the amount no longer claims to be this month's. The whole
            // account is stated in the Whole-time History.
            delinquencySpanLabel: RollingYearLabel(year, month),
            endedOccupancies: endedThisPeriod);

        return Result<FollowUpQueueDto>.Success(dto);
    }

    /// <summary>"12 months to July 2026" — the span a rolling delinquency figure actually covers, ending with the
    /// last month that has closed, since a month still in progress is never counted as unpaid.</summary>
    private static string RollingYearLabel(int year, int month)
    {
        var anchor = new DateOnly(year, month, 1);
        var today = PhilippineTime.Today;
        var lastElapsed = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var end = anchor.AddMonths(-1) < lastElapsed ? anchor.AddMonths(-1) : lastElapsed;
        return $"12 months to {end.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
