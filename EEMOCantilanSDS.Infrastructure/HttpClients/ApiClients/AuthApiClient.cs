using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class AuthApiClient(HttpClient http) : HandleResponse(http), IAuthApiClient
{
    public async Task<Result<TokenResponseDto>> LoginAsync(LoginCommand command) => 
        await PostAsync<LoginCommand, TokenResponseDto>("api/AdminAuth/login", command);

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command) => 
        await PostAsync<RefreshTokenCommand, TokenResponseDto>("api/AdminAuth/refresh-token", command);

    public async Task LogoutAsync(string refreshToken) =>
        await PostAsync("api/AdminAuth/logout", new RefreshTokenCommand { RefreshToken = refreshToken });
}
