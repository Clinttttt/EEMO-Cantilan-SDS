using EEMOCantilanSDS.Domain.Common;
using MediatR;
using System;

namespace EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;

public record ToggleStallStatusCommand(Guid StallId, bool Close) : IRequest<Result<bool>>;
