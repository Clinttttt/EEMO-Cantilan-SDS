using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;

public class GetStallHoldersListQueryHandler(
    IStallRepository stallRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions)
    : IRequestHandler<GetStallHoldersListQuery, Result<StallHoldersListDto>>
{
    public async Task<Result<StallHoldersListDto>> Handle(GetStallHoldersListQuery request, CancellationToken ct)
    {
        var key = EemoCacheKeys.StallHolderList(
            tenantContext.TenantCode,
            request.FacilityCode,
            request.Section,
            request.SearchTerm);
        var regions = EemoCacheRegions.StallHolderListRegions(tenantContext.TenantCode);
        var result = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.StallHolderListTtl,
            token => stallRepository.GetStallHoldersListAsync(
                request.FacilityCode,
                request.Section,
                request.SearchTerm,
                token),
            ct);

        return Result<StallHoldersListDto>.Success(result);
    }
}
