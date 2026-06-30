using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class SettingsApiClient : HandleResponse, ISettingsApiClient
{
    public SettingsApiClient(HttpClient http) : base(http)
    {
    }

    public async Task<Result<SystemSettingsDto>> GetSystemSettingsAsync() =>
        await GetAsync<SystemSettingsDto>("api/Settings");
}
