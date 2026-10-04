using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The prospective source boundary for Tabo and Slaughterhouse Collector Mobile collection (IA-051 pattern, as Transportation).
/// Legacy TpmAttendance / SlaughterTransaction rows are history and stay readable and unchanged. From the business date the Head
/// enables the governed service for Mobile, a Collector's new collection is a canonical Collection with an SRC and the Collector's
/// legacy writer refuses, so one real-world payment can never exist on both paths. Admin/Web entry is unchanged.
/// </summary>
public sealed class GovernedCanonicalAuthority(IAppDbContext db, ICurrentMunicipalityAccessor municipality)
{
    public async Task<bool> IsCanonicalForCollectorAsync(string operationCode, DateOnly businessDate, CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty) return false;
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.OperationCode == operationCode, ct);
        if (service is null) return false;
        var versions = await db.GovernedServiceSettings.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
        return GovernedServiceSetting.Resolve(versions, businessDate) is { IsEnabled: true, MobileEnabled: true };
    }

    /// <summary>
    /// The accountable-instrument policy in force for a classification on a business date (OR or CT), for showing the collector
    /// which instrument the collection falls under. It is never a serial and never chosen by the collector. Null when none is in force.
    /// </summary>
    public async Task<RevenueInstrumentType?> ResolveInstrumentAsync(string classificationCode, DateOnly businessDate, CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty) return null;
        return await (
            from policy in db.RevenueClassificationPolicies.AsNoTracking()
            join classification in db.RevenueClassifications.AsNoTracking() on policy.RevenueClassificationId equals classification.Id
            where policy.MunicipalityId == tenantId && classification.SemanticCode == classificationCode && classification.IsActive
                && policy.BusinessContext == RevenuePolicyContext.Default && policy.EffectiveDate <= businessDate
            orderby policy.EffectiveDate descending
            select policy.PermittedInstrumentType).FirstOrDefaultAsync(ct);
    }
}
