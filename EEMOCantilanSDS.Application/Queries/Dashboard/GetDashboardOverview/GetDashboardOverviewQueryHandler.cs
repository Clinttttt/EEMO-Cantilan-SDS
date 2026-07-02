using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Dashboard;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Dashboard.GetDashboardOverview;

public class GetDashboardOverviewQueryHandler(
    IDashboardRepository dashboardRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions)
    : IRequestHandler<GetDashboardOverviewQuery, Result<DashboardOverviewDto>>
{
    public async Task<Result<DashboardOverviewDto>> Handle(GetDashboardOverviewQuery request, CancellationToken ct)
    {
        var key = EemoCacheKeys.DashboardOverview(tenantContext.TenantCode, request.Year, request.Month);
        var regions = EemoCacheRegions.DashboardOverviewRegions(tenantContext.TenantCode, request.Year, request.Month);
        var overview = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.DashboardOverviewTtl,
            token => dashboardRepository.GetOverviewAsync(request.Year, request.Month, token),
            ct);

        return Result<DashboardOverviewDto>.Success(overview);
    }
}
