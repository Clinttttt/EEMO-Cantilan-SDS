using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IAuthApiClient
{
    Task<Result<TokenResponseDto>> LoginAsync(LoginCommand command);
    Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command);
    Task LogoutAsync(string refreshToken);
}
