using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;

public class GetFacilityHistoryQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IFacilityRepository facilityRepository
) : IRequestHandler<GetFacilityHistoryQuery, Result<FacilityHistoryDto>>
{
    public async Task<Result<FacilityHistoryDto>> Handle(GetFacilityHistoryQuery request, CancellationToken ct)
    {
        var facility = await facilityRepository.GetByCodeAsync(request.FacilityCode, ct);
        if (facility == null)
            return Result<FacilityHistoryDto>.NotFound();

        var history = await reportsRepository.GetFacilityHistoryAsync(request.FacilityCode, request.Year, ct);
        return Result<FacilityHistoryDto>.Success(history);
    }
}
