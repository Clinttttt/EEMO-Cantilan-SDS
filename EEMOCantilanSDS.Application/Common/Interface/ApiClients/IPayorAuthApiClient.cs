using EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Payor authentication calls (activation, login, refresh, logout). Registered without the
/// authorization/refresh delegating handlers, mirroring <see cref="IAuthApiClient"/>.</summary>
public interface IPayorAuthApiClient
{
    Task<Result<TokenResponseDto>> ActivateAsync(ActivatePayorAccountCommand command);
    Task<Result<TokenResponseDto>> LoginAsync(PayorLoginCommand command);
    Task<Result<TokenResponseDto>> RefreshTokenAsync(RefreshTokenCommand command);
    Task LogoutAsync(string refreshToken);
}
