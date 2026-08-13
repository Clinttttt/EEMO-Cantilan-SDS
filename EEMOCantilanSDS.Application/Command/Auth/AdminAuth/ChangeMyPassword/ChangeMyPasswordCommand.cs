using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;

/// <summary>
/// The signed-in administrator replaces their own password.
///
/// <para>
/// Used both freely and to satisfy a required change: when the office issues a password, the account is flagged and the
/// portal will not let it do anything else until this succeeds. Returns a fresh token pair because the requirement travels
/// on the token as a claim — without new tokens the user would still be told to change a password they had just changed.
/// </para>
/// </summary>
/// <param name="CurrentPassword">
/// Proof it is really them. Required even during a forced change: the password may have been handed over on paper, and the
/// person at the keyboard is not necessarily the person it was issued to.
/// </param>
public record ChangeMyPasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<Result<TokenResponseDto>>;
