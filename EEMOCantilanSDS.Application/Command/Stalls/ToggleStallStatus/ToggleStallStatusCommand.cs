using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;

public record ToggleStallStatusCommand(Guid StallId, bool Close) : IRequest<Result<bool>>;
