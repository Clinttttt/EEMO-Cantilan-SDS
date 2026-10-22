using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>
/// Own-account security actions that REQUIRE a bearer token.
/// Never register on the anonymous login/refresh HTTP client.
/// </summary>
public interface IAccountSecurityApiClient
{
    Task<Result<TokenResponseDto>> ChangeMyPasswordAsync(ChangeMyPasswordCommand command);
}
