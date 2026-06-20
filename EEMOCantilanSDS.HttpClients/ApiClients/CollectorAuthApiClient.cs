using EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.HttpClients;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class CollectorAuthApiClient(HttpClient http) : HandleResponse(http), ICollectorAuthApiClient
{
    public async Task<Result<TokenResponseDto>> LoginAsync(CollectorLoginCommand command) =>
        await PostAsync<CollectorLoginCommand, TokenResponseDto>("api/CollectorAuth/login", command);

    public async Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command) =>
        await PostAsync<RefreshTokenCommand, TokenResponseDto>("api/CollectorAuth/refresh-token", command);

    public async Task<Result<bool>> LogoutAsync(RefreshTokenCommand command) =>
        await PostAsync<RefreshTokenCommand, bool>("api/CollectorAuth/logout", command);
}
