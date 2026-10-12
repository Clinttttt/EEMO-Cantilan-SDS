using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;

/// <summary>
/// A Head invites an Admin to set their own password. No password is chosen, typed or shared: the account is created INACTIVE
/// and becomes usable only when its owner follows the one-time link.
/// </summary>
public record InviteAdminCommand(string FullName, string Username, string Email, AdminRole Role)
    : IRequest<Result<AdminInvitationDto>>;
