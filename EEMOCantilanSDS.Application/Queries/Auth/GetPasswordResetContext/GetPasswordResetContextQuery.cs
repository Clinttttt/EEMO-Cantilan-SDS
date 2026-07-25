using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetPasswordResetContext
{
    /// <summary>
    /// Resolves which account a password-reset token belongs to, so the reset page can state the username,
    /// office and municipality before the user sets a new password.
    /// <para>
    /// This matters because one mailbox can hold links for accounts in several LGUs (email uniqueness is
    /// per-LGU): without it, a Head with two accounts cannot tell which link they opened, and may reset the
    /// wrong one. Anonymous (the token is the credential) and returns a generic failure for any bad token.
    /// </para>
    /// </summary>
    public record GetPasswordResetContextQuery(string Token) : IRequest<Result<TokenAccountContextDto>>;
}
