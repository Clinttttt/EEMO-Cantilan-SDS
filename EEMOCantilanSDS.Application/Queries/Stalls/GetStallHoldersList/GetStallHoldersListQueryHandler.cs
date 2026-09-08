using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;

public class GetStallHoldersListQueryHandler(
    IStallRegisterQueries stallRegister,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions)
    : IRequestHandler<GetStallHoldersListQuery, Result<StallHoldersListDto>>
{
    public async Task<Result<StallHoldersListDto>> Handle(GetStallHoldersListQuery request, CancellationToken ct)
    {
        // A year that is not a year is refused, not thrown over. `?year=0` reached DateOnly and returned 500 from a report endpoint —
        // found by audit 2026-09-07. The bounds and the wording mirror GetCollectableDaysQueryHandler, which had this rule already;
        // there is no reason for two report queries to disagree about what a year is.
        if (request.Year is { } year && year is < 1990 or > 2200)
            return Result<StallHoldersListDto>.Failure("That is not a real year.", ResultStatus.Invalid);

        var key = EemoCacheKeys.StallHolderList(
            tenantContext.TenantCode,
            request.FacilityCode,
            request.Section,
            request.SearchTerm,
            request.Year);
        var regions = EemoCacheRegions.StallHolderListRegions(tenantContext.TenantCode);
        var result = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.StallHolderListTtl,
            token => stallRegister.GetStallHoldersListAsync(
                request.FacilityCode,
                request.Section,
                request.SearchTerm,
                request.Year,
                token),
            ct);

        return Result<StallHoldersListDto>.Success(result);
    }
}
