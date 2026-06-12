using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;

public record ResetAdminPasswordCommand(Guid AdminId, string NewPassword, string ConfirmPassword) : IRequest<Result<bool>>;
