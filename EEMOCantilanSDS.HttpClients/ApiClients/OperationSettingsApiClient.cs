using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class OperationSettingsApiClient(HttpClient http) : HandleResponse(http), IOperationSettingsApiClient
{
    public async Task<Result<TenantOperationSettingsDto>> GetAsync() =>
        await GetAsync<TenantOperationSettingsDto>("api/operation-settings");

    public async Task<Result<TenantOperationSettingsDto>> UpdateAsync(string operationCode, UpdateTenantOperationSettingRequest request) =>
        await PutAsync<UpdateTenantOperationSettingRequest, TenantOperationSettingsDto>(
            $"api/operation-settings/{Uri.EscapeDataString(operationCode)}", request);
}
