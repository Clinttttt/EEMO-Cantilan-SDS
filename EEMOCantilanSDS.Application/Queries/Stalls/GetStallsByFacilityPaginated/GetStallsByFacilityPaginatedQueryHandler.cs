using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacilityPaginated;

public class GetStallsByFacilityPaginatedQueryHandler(IStallRepository stallRepository) 
    : IRequestHandler<GetStallsByFacilityPaginatedQuery, Result<CursorPagedResult<StallDto>>>
{
    public async Task<Result<CursorPagedResult<StallDto>>> Handle(GetStallsByFacilityPaginatedQuery request, CancellationToken ct)
    {
        var result = await stallRepository.GetStallsByFacilityPaginatedAsync(
            request.FacilityCode, 
            request.Section, 
            request.Cursor, 
            request.PageSize, 
            ct);
        
        return Result<CursorPagedResult<StallDto>>.Success(result);
    }
}
