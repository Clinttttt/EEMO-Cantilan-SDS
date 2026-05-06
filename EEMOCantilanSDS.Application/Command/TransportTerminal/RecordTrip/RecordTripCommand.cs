using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;

public record RecordTripCommand(
    Guid TransporterId,
    string DriverName,
    string PlateNumber,
    string Route,
    string ORNumber,
    string? Remarks
) : IRequest<Result<TrmTripDto>>;
