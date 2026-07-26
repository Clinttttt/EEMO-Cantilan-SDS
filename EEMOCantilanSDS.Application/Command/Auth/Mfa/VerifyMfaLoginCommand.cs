using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.Mfa
{
    /// <summary>
    /// Completes a two-factor sign-in: exchanges the short-lived challenge from the password step plus an
    /// authenticator code (or a single-use recovery code) for a real session.
    /// <para>
    /// Anonymous — the challenge is the credential — and the ONLY way an MFA-enabled account obtains tokens.
    /// </para>
    /// </summary>
    public record VerifyMfaLoginCommand(string ChallengeToken, string Code) : IRequest<Result<TokenResponseDto>>;
}
