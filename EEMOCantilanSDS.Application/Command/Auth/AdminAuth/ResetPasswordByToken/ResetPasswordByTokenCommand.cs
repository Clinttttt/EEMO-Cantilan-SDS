using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken
{
    /// <summary>
    /// Completes a self-service password reset: the user opens the one-time link emailed to their verified
    /// address and chooses a new password. Anonymous — the token is the only credential — so every failure
    /// returns one generic message (no account enumeration, no hint about which part was wrong).
    /// </summary>
    public record ResetPasswordByTokenCommand(string Token, string NewPassword) : IRequest<Result<bool>>;
}
