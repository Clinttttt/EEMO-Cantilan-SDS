using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken
{
    /// <summary>
    /// Completes a provisioned account's activation: the user opens their secure activation link, supplies the
    /// one-time token, and sets their own password. On success the account is activated. Anonymous flow — the
    /// token is the only credential — so failures return a generic message (no account enumeration).
    /// </summary>
    public record SetAdminPasswordByTokenCommand(string Token, string NewPassword) : IRequest<Result<bool>>;
}
