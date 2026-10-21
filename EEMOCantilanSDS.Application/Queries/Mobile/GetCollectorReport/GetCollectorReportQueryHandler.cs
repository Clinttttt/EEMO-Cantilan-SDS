using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;

public class GetCollectorReportQueryHandler(
    ICollectorRepository collectorRepository,
    // Both, and honestly so: this one loads the collector's account to know their assigned facilities, then reads their
    // report. The account lookup is not a projection and the projection is not an account.
    ICollectorMobileQueries mobileQueries,
    // The collector's posted canonical Collections (governed operations, obligations, penalties, converted rent and
    // utilities): the same facts their Position sums, so the report and the Position cannot disagree.
    ICollectorCollectionFacts canonicalFacts,
    ICurrentUserService currentUser, IClock clock, NpmRentMonthWorkflow? monthly = null) : IRequestHandler<GetCollectorReportQuery, Result<MobileCollectorReportDto>>
{
    public async Task<Result<MobileCollectorReportDto>> Handle(GetCollectorReportQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileCollectorReportDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileCollectorReportDto>.NotFound();

        var assignedFacilities = collector.FacilityAssignments
            .Select(a => a.FacilityCode)
            .Distinct()
            .ToList();

        if (request.Facility.HasValue)
        {
            if (!assignedFacilities.Contains(request.Facility.Value))
                return Result<MobileCollectorReportDto>.Forbidden();

            assignedFacilities = [request.Facility.Value];
        }

        var monthStart = new DateOnly(request.Year, request.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var today = clock.PhilippineToday;
        var effectiveEnd = monthStart.Year == today.Year && monthStart.Month == today.Month && today < monthEnd
            ? today
            : monthEnd;

        var report = await mobileQueries.GetCollectorReportAsync(
            collectorId,
            assignedFacilities,
            monthStart,
            effectiveEnd,
            ct);

        if (monthly is not null)
        {
            var payees = report.Payees.ToArray();
            var changed = false;
            for (var i = 0; i < payees.Length; i++)
            {
                var row = payees[i];
                if (row.FacilityCode != FacilityCode.NPM || row.StallId is not { } stallId) continue;
                var monthlyReport = await monthly.ReadReportPositionAsync(stallId, monthStart, effectiveEnd, ct);
                if (monthlyReport is null) continue;
                var position = monthlyReport.Position;
                changed = true;
                var assessed = monthlyReport.EarnedLiability;
                var paid = position.LegacyCash + position.CanonicalCash;
                var balance = Math.Max(0, assessed - paid);
                payees[i] = row with { AssessedAmount = assessed, AmountPaid = paid, Balance = balance,
                    Status = assessed > 0 && balance == 0 ? PaymentStatus.Paid : paid > 0 ? PaymentStatus.Partial : PaymentStatus.Unpaid };
            }
            if (changed) report = report with { Payees = payees,
                Totals = report.Totals with { OutstandingAmount = payees.Sum(x => x.Balance),
                    PaidCount = payees.Count(x => x.Status == PaymentStatus.Paid), PartialCount = payees.Count(x => x.Status == PaymentStatus.Partial),
                    UnpaidCount = payees.Count(x => x.Status == PaymentStatus.Unpaid) },
                Facilities = report.Facilities.Select(f => f with {
                    OutstandingAmount = payees.Where(x => x.FacilityCode == f.FacilityCode).Sum(x => x.Balance),
                    PaidCount = payees.Count(x => x.FacilityCode == f.FacilityCode && x.Status == PaymentStatus.Paid),
                    PartialCount = payees.Count(x => x.FacilityCode == f.FacilityCode && x.Status == PaymentStatus.Partial),
                    UnpaidCount = payees.Count(x => x.FacilityCode == f.FacilityCode && x.Status == PaymentStatus.Unpaid) }).ToArray() };
        }

        // Canonical money is selected by its own Philippine business date over the whole selected month, inclusive — the
        // date the collection answers for, never a sync or device timestamp. A failed read is a failed report, never ₱0.
        var canonical = await canonicalFacts.GetMyCollectionsAsync(monthStart, monthEnd, ct);
        if (!canonical.IsSuccess || canonical.Value is null)
            return Result<MobileCollectorReportDto>.Failure(
                canonical.Error ?? "Your posted collections could not be read.", canonical.Status);

        return Result<MobileCollectorReportDto>.Success(CollectorReportComposer.Compose(report, canonical.Value));
    }
}
