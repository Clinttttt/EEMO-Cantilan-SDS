using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Caching;

public interface IEemoCacheInvalidator
{
    Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default);
    Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default);
    Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default);
    Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default);
    Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops EVERY cached view belonging to one office.
    ///
    /// <para>
    /// For a change that replaces data wholesale rather than editing a known period: a tenant RESTORE. A restore can
    /// rewrite any row of any year, so no period, facility or region can be named in advance — and until this existed the
    /// restore invalidated nothing at all, so an office that had just rolled its data back kept being shown the figures
    /// it had rolled back from until each cached view happened to expire. Reported from use: a vendor removed by a
    /// restore still counted on its facility page until the page was reloaded.
    /// </para>
    /// </summary>
    Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default);
}
