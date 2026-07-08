using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;

public class GetFacilityReportsQueryHandler(
    IFacilityReportsRepository reportsRepository,
    IFacilityRepository facilityRepository,
    IEemoAppCache cache,
    ITenantContext tenantContext,
    EemoCacheOptions cacheOptions
) : IRequestHandler<GetFacilityReportsQuery, Result<FacilityReportsDto>>
{
    public async Task<Result<FacilityReportsDto>> Handle(GetFacilityReportsQuery request, CancellationToken ct)
    {
        // Verify facility exists
        var facility = await facilityRepository.GetByCodeAsync(request.FacilityCode, ct);
        if (facility == null)
            return Result<FacilityReportsDto>.NotFound();

        // Cache the (expensive) aggregation per tenant/facility/period so repeated opens are instant.
        // Invalidated by the same period/facility regions the other reports use, so recorded payments
        // refresh it. The aggregation itself is unchanged, so figures are identical.
        var key = EemoCacheKeys.FacilityReport(
            tenantContext.TenantCode, request.FacilityCode, request.Period, request.Year, request.Month, request.WeekNumber);
        var regions = EemoCacheRegions.FacilityReportRegions(
            tenantContext.TenantCode, request.FacilityCode, request.Year, request.Month);

        var report = await cache.GetOrCreateAsync(
            key,
            regions,
            cacheOptions.FacilityReportTtl,
            token => reportsRepository.GetFacilityReportsAsync(
                request.FacilityCode,
                request.Period,
                request.Year,
                request.Month,
                request.WeekNumber,
                token),
            ct);

        return Result<FacilityReportsDto>.Success(report);
    }
}
