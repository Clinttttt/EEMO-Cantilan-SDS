using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IOperationSettingsApiClient
{
    Task<Result<TenantOperationSettingsDto>> GetAsync();

    Task<Result<TenantOperationSettingsDto>> UpdateAsync(string operationCode, UpdateTenantOperationSettingRequest request);
}
