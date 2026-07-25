using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Dtos;
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
}
