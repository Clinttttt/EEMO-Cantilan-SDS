using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.VerifyEmail;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IAuthApiClient
{
    Task<Result<TokenResponseDto>> LoginAsync(LoginCommand command);

    /// <summary>
    /// The signed-in administrator replaces their own password, receiving a fresh session. Returns tokens because the
    /// requirement to change travels on the token: without new ones the portal would keep asking.
    /// </summary>
    Task<Result<TokenResponseDto>> ChangeMyPasswordAsync(ChangeMyPasswordCommand command);
    Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command);
    Task LogoutAsync(string refreshToken);

    /// <summary>Requests a password-reset link. Always succeeds (neutral, enumeration-safe).</summary>
    Task<Result<bool>> RequestPasswordResetAsync(RequestPasswordResetCommand command);

    /// <summary>Completes a password reset using the one-time token from the emailed link.</summary>
    Task<Result<bool>> ResetPasswordByTokenAsync(ResetPasswordByTokenCommand command);

    /// <summary>Resolves which account (username / LGU) a password-reset token belongs to.</summary>
    Task<Result<TokenAccountContextDto>> GetPasswordResetContextAsync(string token);

    /// <summary>Confirms an email address using the one-time token from the confirmation link.</summary>
    Task<Result<VerifiedAccountDto>> VerifyEmailAsync(VerifyEmailCommand command);

    /// <summary>Completes a two-factor sign-in (challenge from the password step + authenticator code).</summary>
    Task<Result<TokenResponseDto>> VerifyMfaLoginAsync(VerifyMfaLoginCommand command);
}
