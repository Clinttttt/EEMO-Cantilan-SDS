using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;

public record UpdateAdminCommand(
    Guid AdminId,
    string FullName,
    string Email,
    AdminRole Role) : IRequest<Result<bool>>;
