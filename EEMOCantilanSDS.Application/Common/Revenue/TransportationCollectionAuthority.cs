using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The source boundary for Transportation / Parking (IA-050). Legacy TRM trips are history and stay readable and unchanged.
/// From the moment the Head enables the canonical Transportation Cash Ticket service, new collections use it, and the legacy
/// trip writers refuse, so one real-world collection can never be recorded on both paths. No date is backdated: the boundary
/// is the enable date the Head sets when the approved release goes live.
/// </summary>
public sealed class TransportationCollectionAuthority(IAppDbContext db, ICurrentMunicipalityAccessor municipality)
{
    public async Task<bool> IsCanonicalAsync(DateOnly businessDate, CancellationToken ct = default)
    {
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty) return false;
        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.OperationCode == CollectorOperationCodes.Transportation, ct);
        if (service is null) return false;
        var versions = await db.GovernedServiceSettings.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
        return GovernedServiceSetting.Resolve(versions, businessDate) is { IsEnabled: true };
    }
}
