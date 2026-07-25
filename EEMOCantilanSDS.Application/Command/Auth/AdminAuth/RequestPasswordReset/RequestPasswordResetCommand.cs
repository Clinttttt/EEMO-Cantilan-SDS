using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset
{
    /// <summary>
    /// Starts a self-service password reset: the user supplies their username or email and, when signing in
    /// through a scoped LGU URL, that municipality's code so the lookup is tenant-correct.
    /// <para>
    /// Anonymous and deliberately ENUMERATION-SAFE: the handler always reports the same neutral success,
    /// whether or not an account matched, so this endpoint can never be used to discover which usernames or
    /// email addresses exist. A reset link is emailed only when a matching account with a VERIFIED email is
    /// found.
    /// </para>
    /// </summary>
    public record RequestPasswordResetCommand(string UsernameOrEmail, string? MunicipalityCode = null)
        : IRequest<Result<bool>>;
}
