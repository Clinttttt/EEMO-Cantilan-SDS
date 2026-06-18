using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;

public class GetCollectorReportQueryHandler(
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetCollectorReportQuery, Result<MobileCollectorReportDto>>
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
        var today = PhilippineTime.Today;
        var effectiveEnd = monthStart.Year == today.Year && monthStart.Month == today.Month && today < monthEnd
            ? today
            : monthEnd;

        var report = await collectorRepository.GetCollectorReportAsync(
            collectorId,
            assignedFacilities,
            monthStart,
            effectiveEnd,
            ct);

        return Result<MobileCollectorReportDto>.Success(report);
    }
}
