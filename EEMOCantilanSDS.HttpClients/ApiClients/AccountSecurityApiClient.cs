using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

/// <summary>
/// Bearer-authenticated account-security requests, using the portal's standard
/// authorization and token-refresh pipeline (unlike anonymous AuthApiClient).
/// </summary>
public class AccountSecurityApiClient(HttpClient http) : HandleResponse(http), IAccountSecurityApiClient
{
    public async Task<Result<TokenResponseDto>> ChangeMyPasswordAsync(ChangeMyPasswordCommand command) =>
        await PostAsync<ChangeMyPasswordCommand, TokenResponseDto>("api/AdminAuth/change-my-password", command);
}
