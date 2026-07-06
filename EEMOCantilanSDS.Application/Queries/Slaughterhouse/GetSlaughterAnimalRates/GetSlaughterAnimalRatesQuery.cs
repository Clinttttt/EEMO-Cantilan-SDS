using System.Collections.Generic;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterAnimalRates
{
    /// <summary>
    /// Lists the caller LGU's custom slaughterhouse animal types + default rates. Results are automatically
    /// scoped to the caller's municipality by the global query filter. <paramref name="ActiveOnly"/> hides
    /// retired animals (the default), for populating the SLH record screen.
    /// </summary>
    public record GetSlaughterAnimalRatesQuery(bool ActiveOnly = true)
        : IRequest<Result<IReadOnlyList<SlaughterAnimalRateDto>>>;
}
