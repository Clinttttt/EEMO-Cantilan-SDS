using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.SendAdminEmailVerification
{
    /// <summary>
    /// Head-triggered: (re)send the email-confirmation link for one admin account. Used when the original
    /// message was missed, or for accounts created before verification existed — confirming the address is
    /// what enables that admin to reset their own password later.
    /// </summary>
    public record SendAdminEmailVerificationCommand(Guid AdminId) : IRequest<Result<bool>>;
}
