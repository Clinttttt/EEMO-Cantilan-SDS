using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTrmHistory;

public record GetTrmHistoryQuery(int Year) : IRequest<Result<TrmHistoryDto>>;
