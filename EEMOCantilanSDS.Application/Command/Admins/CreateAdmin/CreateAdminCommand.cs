using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;

public record CreateAdminCommand(
    string FullName,
    string Username,
    string Email,
    string Password,
    AdminRole Role) : IRequest<Result<AdminDto>>;
