using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.SendMyEmailVerification
{
    /// <summary>
    /// Sends the signed-in account its own email-confirmation link.
    ///
    /// <para>
    /// Carries no id: the subject is whoever is asking, taken from the token, so this can never be pointed at another
    /// account. The Head-triggered <c>SendAdminEmailVerification</c> exists for staff whose address was never confirmed,
    /// but it reaches only a municipality's own admins — the platform operator is deliberately excluded from that roster,
    /// and is also the one account with nobody above it to act for it. Confirming an address is what allows a self-service
    /// password reset later, so without this the operator could never obtain one.
    /// </para>
    /// </summary>
    public record SendMyEmailVerificationCommand : IRequest<Result<bool>>;
}
