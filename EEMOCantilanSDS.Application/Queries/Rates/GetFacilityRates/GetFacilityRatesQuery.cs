using System.Collections.Generic;
using EEMOCantilanSDS.Application.Dtos.Rates;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Rates.GetFacilityRates
{
    /// <summary>
    /// Returns the caller LGU's currently-effective fixed rates (latest effective row per facility + key,
    /// as of today). Auto-scoped to the caller's municipality. Used by the portal to display current fees
    /// and pre-fill the metered-utility default rate.
    /// </summary>
    public record GetFacilityRatesQuery() : IRequest<Result<IReadOnlyList<FacilityRateDto>>>;
}
