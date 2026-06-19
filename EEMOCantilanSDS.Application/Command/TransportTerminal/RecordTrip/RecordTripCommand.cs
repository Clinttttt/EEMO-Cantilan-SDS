using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;

public record RecordTripCommand(
    Guid? TransporterId,
    string DriverName,
    string PlateNumber,
    string Route,
    string ORNumber,
    string? Remarks,
    string? Organization = null,
    // Offline-sync: the time the trip was actually recorded offline (null = now), and the client
    // idempotency key (null online) so a replayed queued trip is created once.
    DateTime? OccurredAt = null,
    Guid? ClientOperationId = null
) : IRequest<Result<TrmTripDto>>;
