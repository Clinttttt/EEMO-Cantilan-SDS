using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate
{
    public class SetFacilityRateCommandHandler(
        IAppDbContext context,
        IEemoCacheInvalidator cacheInvalidator,
        ITenantContext tenantContext) : IRequestHandler<SetFacilityRateCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(SetFacilityRateCommand request, CancellationToken ct)
        {
            // Effective today forward — never retroactive, so elapsed periods stay exactly as billed.
            var effective = PhilippineTime.Today;

            // FacilityRates is auto-scoped to the caller's LGU by the global query filter; a new row is
            // stamped to the caller's LGU by the write interceptor.
            var existing = await context.FacilityRates
                .FirstOrDefaultAsync(r => r.FacilityCode == request.FacilityCode
                                       && r.RateKey == request.Key
                                       && r.EffectiveDate == effective, ct);

            if (existing is not null)
                existing.UpdateAmount(request.Amount, "RateEdit");
            else
                context.FacilityRates.Add(
                    FacilityRate.Create(request.FacilityCode, request.Key, request.Amount, effective, createdBy: "RateEdit"));

            await context.SaveChangesAsync(ct);

            // A rate change affects the facility's current-period reports/dashboards.
            await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
                tenantContext.TenantCode, request.FacilityCode, effective.Year, effective.Month, ct);

            // And the stallholder roster, which is reference data rather than a period view. Every money column
            // on that sheet - monthly rental, actual monthly, whole year - is derived from the fee-rate snapshot,
            // so leaving it cached meant the office could change a rate and go on being shown the old figures for
            // up to five minutes, with no way to tell which of the two screens was right.
            await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

            return Result<bool>.Success(true);
        }
    }
}
