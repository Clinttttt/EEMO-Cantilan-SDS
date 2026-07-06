using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OrSeries.GetOrSeriesSuggestion
{
    public class GetOrSeriesSuggestionQueryHandler(IAppDbContext context)
        : IRequestHandler<GetOrSeriesSuggestionQuery, Result<OrSeriesSuggestionDto>>
    {
        public async Task<Result<OrSeriesSuggestionDto>> Handle(GetOrSeriesSuggestionQuery request, CancellationToken ct)
        {
            // Auto-scoped to the caller's municipality (at most one config per LGU).
            var cfg = await context.OrSeriesConfigs.AsNoTracking().FirstOrDefaultAsync(ct);

            if (cfg is null)
                return Result<OrSeriesSuggestionDto>.Success(new OrSeriesSuggestionDto(false, null, null, 0, 0));

            var suggestion = cfg.IsEnabled ? cfg.Peek() : null;
            return Result<OrSeriesSuggestionDto>.Success(
                new OrSeriesSuggestionDto(cfg.IsEnabled, suggestion, cfg.Prefix, cfg.NextNumber, cfg.PadWidth));
        }
    }
}
