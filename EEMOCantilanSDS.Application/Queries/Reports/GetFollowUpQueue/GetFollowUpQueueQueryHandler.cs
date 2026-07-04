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
        var contracts = await stallRepository.GetContractAttentionAsync(DomainRules.ExpiringSoonMonths, ct);
        var utilityBills = await utilityBillRepository.GetForMonthAsync(year, month, ct);

        var dto = FollowUpComposer.Compose(
            year, month, PhilippineTime.Today,
            delinquency, facilityReports, awaitingOr,
            slaughter, trips, attendance, unreceipted, contracts, utilityBills);

        return Result<FollowUpQueueDto>.Success(dto);
    }
}
