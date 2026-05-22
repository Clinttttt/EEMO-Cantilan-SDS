using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;

public class GetFacilityReportsQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IFacilityRepository facilityRepository
) : IRequestHandler<GetFacilityReportsQuery, Result<FacilityReportsDto>>
{
    public async Task<Result<FacilityReportsDto>> Handle(GetFacilityReportsQuery request, CancellationToken ct)
    {
        // Verify facility exists
        var facility = await facilityRepository.GetByCodeAsync(request.FacilityCode, ct);
        if (facility == null)
            return Result<FacilityReportsDto>.NotFound();
        
        // Delegate aggregation to repository
        var report = await reportsRepository.GetFacilityReportsAsync(
            request.FacilityCode,
            request.Period,
            request.Year,
            request.Month,
            request.WeekNumber,
            ct
        );
        
        return Result<FacilityReportsDto>.Success(report);
    }
}
