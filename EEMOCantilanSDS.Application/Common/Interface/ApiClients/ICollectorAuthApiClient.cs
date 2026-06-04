using EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ICollectorAuthApiClient
{
    Task<Result<TokenResponseDto>> LoginAsync(CollectorLoginCommand command);
    Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command);
    Task<Result<bool>> LogoutAsync(RefreshTokenCommand command);
}
