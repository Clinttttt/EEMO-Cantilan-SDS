using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.ResetCollectorPassword;

public record ResetCollectorPasswordCommand(Guid CollectorId, string NewPassword) : IRequest<Result<bool>>;
