using EEMOCantilanSDS.Application.Common.Interface.Time;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate
{
    public class SetFacilityRateCommandHandler(
        IAppDbContext context,
        IEemoCacheInvalidator cacheInvalidator,
        ITenantContext tenantContext, IClock clock) : IRequestHandler<SetFacilityRateCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(SetFacilityRateCommand request, CancellationToken ct)
        {
            // A Public Market declared as standard monthly rental is let at each stall's own contract rent and has no daily or
            // fixed monthly stall rate: filing one would show the office a figure no collection or report ever reads.
            if (request.FacilityCode == FacilityCode.NPM
                && request.Key is FeeRateKey.NpmDailyStall or FeeRateKey.NpmMonthlyStall or FeeRateKey.NpmDailyStallVegetable
                    or FeeRateKey.NpmDailyStallFish or FeeRateKey.NpmDailyStallMeat
                && await context.Facilities.AnyAsync(f => f.Code == FacilityCode.NPM && f.Archetype == BillingArchetype.MonthlyRental, ct))
                return Result<bool>.Failure("This market is rented by the month; its rent is set for each stall, not as a daily or market-wide rate.", ResultStatus.Invalid);

            // Effective today forward — never retroactive, so elapsed periods stay exactly as billed.
            var effective = clock.PhilippineToday;

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
