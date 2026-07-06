using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterAnimalRates
{
    public class GetSlaughterAnimalRatesQueryHandler(IAppDbContext context)
        : IRequestHandler<GetSlaughterAnimalRatesQuery, Result<IReadOnlyList<SlaughterAnimalRateDto>>>
    {
        public async Task<Result<IReadOnlyList<SlaughterAnimalRateDto>>> Handle(
            GetSlaughterAnimalRatesQuery request, CancellationToken ct)
        {
            // The global query filter scopes this to the caller's own municipality.
            var query = context.SlaughterAnimalRates.AsNoTracking();
            if (request.ActiveOnly)
                query = query.Where(a => a.IsActive);

            var rates = await query
                .OrderBy(a => a.AnimalName)
                .Select(a => new SlaughterAnimalRateDto(a.Id, a.AnimalName, a.RatePerHead, a.IsActive))
                .ToListAsync(ct);

            return Result<IReadOnlyList<SlaughterAnimalRateDto>>.Success(rates);
        }
    }
}
