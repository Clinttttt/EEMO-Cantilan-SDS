using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ResendAdminInvitation;

/// <summary>Issues a fresh link for an invitation that has not been accepted. The previous link stops working.</summary>
public record ResendAdminInvitationCommand(Guid AdminId) : IRequest<Result<AdminInvitationDto>>;
