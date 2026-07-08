using EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetActivationContext;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class ActivationApiClient(HttpClient http) : HandleResponse(http), IActivationApiClient
{
    public async Task<Result<bool>> SetPasswordByTokenAsync(SetAdminPasswordByTokenCommand command) =>
        await PostAsync<SetAdminPasswordByTokenCommand, bool>("api/activation/set-password", command);

    public async Task<Result<ActivationContextDto>> GetActivationContextAsync(string token) =>
        await GetAsync<ActivationContextDto>($"api/activation/context/{Uri.EscapeDataString(token)}");
}
