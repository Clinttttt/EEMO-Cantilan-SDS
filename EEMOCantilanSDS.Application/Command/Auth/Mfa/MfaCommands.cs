using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.Mfa
{
    /// <summary>
    /// Starts two-factor enrollment for the SIGNED-IN user and returns the secret to add to an authenticator
    /// app (QR + manual key). Re-entering the current password is required so a hijacked session cannot bind
    /// an attacker's authenticator to the account.
    /// <para>Nothing is enforced until <see cref="ConfirmMfaEnrollmentCommand"/> succeeds.</para>
    /// </summary>
    public record BeginMfaEnrollmentCommand(string CurrentPassword) : IRequest<Result<MfaEnrollmentDto>>;

    /// <summary>
    /// Activates two-factor sign-in by proving the authenticator works. On success the single-use recovery
    /// codes are returned — the only time they are ever shown.
    /// </summary>
    public record ConfirmMfaEnrollmentCommand(string Code) : IRequest<Result<MfaRecoveryCodesDto>>;

    /// <summary>
    /// Turns two-factor off for the signed-in user. Requires the current password AND a valid code (or a
    /// recovery code), so possession of a session alone cannot strip the second factor.
    /// </summary>
    public record DisableMfaCommand(string CurrentPassword, string Code) : IRequest<Result<bool>>;

    /// <summary>
    /// Issues a fresh set of recovery codes, invalidating every previous one. Requires the current password.
    /// </summary>
    public record RegenerateRecoveryCodesCommand(string CurrentPassword) : IRequest<Result<MfaRecoveryCodesDto>>;
}
