using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.RevokeAdminInvitation;

/// <summary>Withdraws an invitation that has not been accepted: the link stops working and the pending account is removed.</summary>
public record RevokeAdminInvitationCommand(Guid AdminId) : IRequest<Result<bool>>;
