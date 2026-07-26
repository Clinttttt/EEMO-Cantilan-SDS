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

    // ── Two-factor (own account) ──
    Task<Result<MfaStatusDto>> GetMfaStatusAsync();    Task<Result<MfaEnrollmentDto>> BeginMfaEnrollmentAsync(BeginMfaEnrollmentCommand command);
    Task<Result<MfaRecoveryCodesDto>> ConfirmMfaEnrollmentAsync(ConfirmMfaEnrollmentCommand command);
    Task<Result<bool>> DisableMfaAsync(DisableMfaCommand command);
    Task<Result<MfaRecoveryCodesDto>> RegenerateRecoveryCodesAsync(RegenerateRecoveryCodesCommand command);

    /// <summary>Completes a two-factor sign-in (challenge from the password step + authenticator code).</summary>
    Task<Result<TokenResponseDto>> VerifyMfaLoginAsync(VerifyMfaLoginCommand command);

    // ── Platform-operator two-factor recovery ──
    /// <summary>Every MFA-enrolled account across all LGUs (platform operator only).</summary>
    Task<Result<IReadOnlyList<MfaEnrolledAccountDto>>> GetMfaEnrolledAccountsAsync();

    /// <summary>Clears an account's two-factor when its owner lost both device and recovery codes.</summary>
    Task<Result<bool>> ResetUserMfaAsync(ResetUserMfaCommand command);
}
