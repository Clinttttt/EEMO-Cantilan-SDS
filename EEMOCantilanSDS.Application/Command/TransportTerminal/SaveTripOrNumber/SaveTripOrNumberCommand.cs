using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.SaveTripOrNumber;

public record SaveTripOrNumberCommand(Guid TripId, string ORNumber) : IRequest<Result<bool>>;
