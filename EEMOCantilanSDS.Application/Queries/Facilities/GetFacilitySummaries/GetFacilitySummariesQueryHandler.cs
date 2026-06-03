using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummaries;

public class GetFacilitySummariesQueryHandler(IFacilityRepository facilityRepository)
    : IRequestHandler<GetFacilitySummariesQuery, Result<IReadOnlyList<FacilitySidebarSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<FacilitySidebarSummaryDto>>> Handle(GetFacilitySummariesQuery request, CancellationToken ct)
    {
        var summaries = await facilityRepository.GetSidebarSummariesAsync(request.Year, request.Month, ct);
        return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(summaries);
    }
}
