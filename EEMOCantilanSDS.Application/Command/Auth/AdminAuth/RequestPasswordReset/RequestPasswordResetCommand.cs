using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset
{
    /// <summary>
    /// Starts a self-service password reset: the user supplies the EMAIL ADDRESS registered on their account
    /// and, when signing in through a scoped LGU URL, that municipality's code so the lookup is tenant-correct.
    /// <para>
    /// Email only (not username) by design: the reset link can only ever be delivered to the registered
    /// address, so asking for that address keeps the form honest about what recovery actually requires — and
    /// it avoids handing out a username→mailbox oracle.
    /// </para>
    /// <para>
    /// Anonymous and deliberately ENUMERATION-SAFE: the handler always reports the same neutral success,
    /// whether or not an account matched, so this endpoint can never be used to discover which email
    /// addresses exist. A reset link is emailed only when a matching account with a VERIFIED email is found.
    /// </para>
    /// </summary>
    public record RequestPasswordResetCommand(string Email, string? MunicipalityCode = null)
        : IRequest<Result<bool>>;
}
