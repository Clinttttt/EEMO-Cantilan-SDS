using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTodayTrips;

public record GetTodayTripsQuery : IRequest<Result<IReadOnlyList<TrmTripDto>>>;
