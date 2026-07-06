using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OrSeries.AdvanceOrSeries
{
    /// <summary>
    /// Advances the caller LGU's OR-series counter by one — called by the portal only after a receipt
    /// actually used the suggested OR number. Returns the new (post-advance) suggestion. OR numbers remain
    /// manually confirmed by the admin; this never stamps OR values onto records.
    /// </summary>
    public record AdvanceOrSeriesCommand() : IRequest<Result<string>>;
}
