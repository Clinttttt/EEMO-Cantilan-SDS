using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ToggleAdminStatus;

public record ToggleAdminStatusCommand(Guid AdminId, bool Activate) : IRequest<Result<bool>>;
