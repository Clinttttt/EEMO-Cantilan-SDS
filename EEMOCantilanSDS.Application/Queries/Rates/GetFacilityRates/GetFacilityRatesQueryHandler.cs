using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Rates;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Rates.GetFacilityRates
{
    public class GetFacilityRatesQueryHandler(IAppDbContext context)
        : IRequestHandler<GetFacilityRatesQuery, Result<IReadOnlyList<FacilityRateDto>>>
    {
        public async Task<Result<IReadOnlyList<FacilityRateDto>>> Handle(GetFacilityRatesQuery request, CancellationToken ct)
        {
            var today = PhilippineTime.Today;

            // Auto-scoped to the caller's LGU by the global query filter. Take rows effective on/before today.
            var rows = await context.FacilityRates
                .AsNoTracking()
                .Where(r => r.EffectiveDate <= today)
                .ToListAsync(ct);

            // Current amount = the latest effective row per (facility, key).
            var current = rows
                .GroupBy(r => new { r.FacilityCode, r.RateKey })
                .Select(g => g.OrderByDescending(r => r.EffectiveDate).First())
                .OrderBy(r => r.FacilityCode).ThenBy(r => r.RateKey)
                .Select(r => new FacilityRateDto(r.FacilityCode, r.RateKey, r.Amount, r.EffectiveDate))
                .ToList();

            return Result<IReadOnlyList<FacilityRateDto>>.Success(current);
        }
    }
}
