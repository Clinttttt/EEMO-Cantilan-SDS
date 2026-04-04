using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;

public class GetStallHoldersListQueryHandler(IStallRepository stallRepository)
    : IRequestHandler<GetStallHoldersListQuery, Result<StallHoldersListDto>>
{
    public async Task<Result<StallHoldersListDto>> Handle(GetStallHoldersListQuery request, CancellationToken ct)
    {
        var result = await stallRepository.GetStallHoldersListAsync(
            request.FacilityCode,
            request.Section,
            request.SearchTerm,
            ct);

        return Result<StallHoldersListDto>.Success(result);
    }
}
