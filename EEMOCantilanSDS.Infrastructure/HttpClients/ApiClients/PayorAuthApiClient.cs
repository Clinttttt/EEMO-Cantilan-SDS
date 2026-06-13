using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class PayorAuthApiClient(HttpClient http) : HandleResponse(http), IPayorAuthApiClient
{
    public async Task<Result<TokenResponseDto>> ActivateAsync(ActivatePayorAccountCommand command) =>
        await PostAsync<ActivatePayorAccountCommand, TokenResponseDto>("api/PayorAuth/activate", command);

    public async Task<Result<TokenResponseDto>> LoginAsync(PayorLoginCommand command) =>
        await PostAsync<PayorLoginCommand, TokenResponseDto>("api/PayorAuth/login", command);

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command) =>
        await PostAsync<RefreshTokenCommand, TokenResponseDto>("api/PayorAuth/refresh-token", command);

    public async Task LogoutAsync(string refreshToken) =>
        await PostAsync("api/PayorAuth/logout", new RefreshTokenCommand { RefreshToken = refreshToken });
}
