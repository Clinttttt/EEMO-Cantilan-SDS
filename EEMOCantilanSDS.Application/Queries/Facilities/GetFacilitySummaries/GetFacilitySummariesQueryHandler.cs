using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummaries;

public class GetFacilitySummariesQueryHandler(
    IFacilityRepository facilityRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions)
    : IRequestHandler<GetFacilitySummariesQuery, Result<IReadOnlyList<FacilitySidebarSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<FacilitySidebarSummaryDto>>> Handle(GetFacilitySummariesQuery request, CancellationToken ct)
    {
        var key = EemoCacheKeys.FacilitySummaries(tenantContext.TenantCode, request.Year, request.Month);
        var regions = EemoCacheRegions.FacilitySummariesRegions(tenantContext.TenantCode, request.Year, request.Month);
        var summaries = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.FacilitySummariesTtl,
            token => facilityRepository.GetSidebarSummariesAsync(request.Year, request.Month, token),
            ct);

        return Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(summaries);
    }
}
