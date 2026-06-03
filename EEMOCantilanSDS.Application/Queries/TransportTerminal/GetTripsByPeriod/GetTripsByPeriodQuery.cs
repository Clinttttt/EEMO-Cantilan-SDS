using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTripsByPeriod;

public record GetTripsByPeriodQuery(int Year, int Month) : IRequest<Result<IReadOnlyList<TrmTripDto>>>;
