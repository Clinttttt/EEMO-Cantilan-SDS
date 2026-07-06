using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OrSeries.GetOrSeriesSuggestion
{
    /// <summary>Returns the caller LGU's suggested next OR number (non-consuming).</summary>
    public record GetOrSeriesSuggestionQuery() : IRequest<Result<OrSeriesSuggestionDto>>;
}
