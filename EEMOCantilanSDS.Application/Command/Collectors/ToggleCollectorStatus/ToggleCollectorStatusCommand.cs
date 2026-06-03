using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.ToggleCollectorStatus;

public record ToggleCollectorStatusCommand(Guid CollectorId, bool Activate) : IRequest<Result<bool>>;
