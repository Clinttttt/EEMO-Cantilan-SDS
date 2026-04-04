using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;

public record CreateFirstAdminCommand(
    string FullName,
    string Username,
    string Email,
    string Password
) : IRequest<Result<bool>>;
