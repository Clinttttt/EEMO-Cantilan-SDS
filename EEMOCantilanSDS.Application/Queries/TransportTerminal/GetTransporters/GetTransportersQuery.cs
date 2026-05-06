using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporters;

public record GetTransportersQuery : IRequest<Result<IReadOnlyList<TrmTransporterListDto>>>;
