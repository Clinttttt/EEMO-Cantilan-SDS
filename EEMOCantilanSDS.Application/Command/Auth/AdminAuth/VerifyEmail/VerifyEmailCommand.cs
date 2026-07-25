using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.VerifyEmail
{
    /// <summary>
    /// Confirms an email address via its one-time link. Anonymous — the token is the credential — and
    /// deliberately narrow: it ONLY marks the address verified. It never activates an account, changes a
    /// password, or grants a session. Any invalid/expired/used token returns one generic message.
    /// </summary>
    public record VerifyEmailCommand(string Token) : IRequest<Result<VerifiedAccountDto>>;
}
