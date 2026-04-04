using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummary;

public class GetFacilitySummaryQueryHandler(IFacilityRepository facilityRepository) : IRequestHandler<GetFacilitySummaryQuery, Result<FacilitySummaryDto>>
{
    public async Task<Result<FacilitySummaryDto>> Handle(GetFacilitySummaryQuery request, CancellationToken ct)
    {
        var summary = await facilityRepository.GetSummaryAsync(request.FacilityCode, request.Year, request.Month, ct);
        return Result<FacilitySummaryDto>.Success(summary);
    }
}
