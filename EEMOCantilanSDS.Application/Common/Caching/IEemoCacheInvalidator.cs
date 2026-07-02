using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Caching;

public interface IEemoCacheInvalidator
{
    Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default);
    Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default);
    Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default);
    Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default);
    Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default);
}
