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

/// <summary>
/// ANONYMOUS auth calls only. This client is registered without the authorization/refresh delegating
/// handlers (it serves sign-in, refresh and logout), so it must never host an <c>[Authorize]</c> endpoint —
/// such a call would carry no bearer token and fail with 401. Authenticated two-factor operations live on
/// <see cref="IMfaApiClient"/>.
/// </summary>
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

    /// <summary>Anonymous by nature: the sign-in challenge is the credential, no session exists yet.</summary>
    public async Task<Result<TokenResponseDto>> VerifyMfaLoginAsync(VerifyMfaLoginCommand command) =>
        await PostAsync<VerifyMfaLoginCommand, TokenResponseDto>("api/AdminAuth/mfa/verify-login", command);
}
