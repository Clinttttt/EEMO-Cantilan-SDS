using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacility;

public class GetStallsByFacilityQueryHandler(IStallRepository stallRepository) : IRequestHandler<GetStallsByFacilityQuery, Result<IReadOnlyList<StallDto>>>
{
    public async Task<Result<IReadOnlyList<StallDto>>> Handle(GetStallsByFacilityQuery request, CancellationToken ct)
    {
        var stalls = await stallRepository.GetStallsByFacilityAsync(request.FacilityCode, request.Section, ct);
        return Result<IReadOnlyList<StallDto>>.Success(stalls);
    }
}
