using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.VerifyEmail;
using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class AuthApiClient(HttpClient http) : HandleResponse(http), IAuthApiClient
{
    public async Task<Result<TokenResponseDto>> LoginAsync(LoginCommand command) => 
        await PostAsync<LoginCommand, TokenResponseDto>("api/AdminAuth/login", command);

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command) => 
        await PostAsync<RefreshTokenCommand, TokenResponseDto>("api/AdminAuth/refresh-token", command);

    public async Task LogoutAsync(string refreshToken) =>
        await PostAsync("api/AdminAuth/logout", new RefreshTokenCommand { RefreshToken = refreshToken });

    public async Task<Result<bool>> RequestPasswordResetAsync(RequestPasswordResetCommand command) =>
        await PostAsync<RequestPasswordResetCommand, bool>("api/AdminAuth/forgot-password", command);

    public async Task<Result<bool>> ResetPasswordByTokenAsync(ResetPasswordByTokenCommand command) =>
        await PostAsync<ResetPasswordByTokenCommand, bool>("api/AdminAuth/reset-password", command);

    public async Task<Result<TokenAccountContextDto>> GetPasswordResetContextAsync(string token) =>
        await GetAsync<TokenAccountContextDto>($"api/AdminAuth/reset-context/{Uri.EscapeDataString(token)}");

    public async Task<Result<VerifiedAccountDto>> VerifyEmailAsync(VerifyEmailCommand command) =>
        await PostAsync<VerifyEmailCommand, VerifiedAccountDto>("api/AdminAuth/verify-email", command);

    // -- Two-factor (own account) --
    public async Task<Result<MfaStatusDto>> GetMfaStatusAsync() =>
        await GetAsync<MfaStatusDto>("api/AdminAuth/mfa/status");

    public async Task<Result<MfaEnrollmentDto>> BeginMfaEnrollmentAsync(BeginMfaEnrollmentCommand command) =>
        await PostAsync<BeginMfaEnrollmentCommand, MfaEnrollmentDto>("api/AdminAuth/mfa/enroll", command);

    public async Task<Result<MfaRecoveryCodesDto>> ConfirmMfaEnrollmentAsync(ConfirmMfaEnrollmentCommand command) =>
        await PostAsync<ConfirmMfaEnrollmentCommand, MfaRecoveryCodesDto>("api/AdminAuth/mfa/confirm", command);

    public async Task<Result<bool>> DisableMfaAsync(DisableMfaCommand command) =>
        await PostAsync<DisableMfaCommand, bool>("api/AdminAuth/mfa/disable", command);

    public async Task<Result<MfaRecoveryCodesDto>> RegenerateRecoveryCodesAsync(RegenerateRecoveryCodesCommand command) =>
        await PostAsync<RegenerateRecoveryCodesCommand, MfaRecoveryCodesDto>("api/AdminAuth/mfa/recovery-codes", command);

    public async Task<Result<TokenResponseDto>> VerifyMfaLoginAsync(VerifyMfaLoginCommand command) =>
        await PostAsync<VerifyMfaLoginCommand, TokenResponseDto>("api/AdminAuth/mfa/verify-login", command);
}
