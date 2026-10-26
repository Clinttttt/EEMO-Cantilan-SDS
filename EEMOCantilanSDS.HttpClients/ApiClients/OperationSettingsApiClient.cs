using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class OperationSettingsApiClient(HttpClient http) : HandleResponse(http), IOperationSettingsApiClient
{
    public async Task<Result<IReadOnlyList<EEMOCantilanSDS.Application.Common.Revenue.TerminalSectionSettingsStore.SectionView>>> GetTerminalSectionsAsync() =>
        await GetAsync<IReadOnlyList<EEMOCantilanSDS.Application.Common.Revenue.TerminalSectionSettingsStore.SectionView>>("api/terminal-sections");
    public async Task<Result<IReadOnlyList<EEMOCantilanSDS.Application.Common.Revenue.TerminalSectionSettingsStore.SectionView>>> UpdateTerminalSectionAsync(EEMOCantilanSDS.Application.Dtos.Settings.TerminalSectionUpdateRequest request) =>
        await PutAsync<EEMOCantilanSDS.Application.Dtos.Settings.TerminalSectionUpdateRequest, IReadOnlyList<EEMOCantilanSDS.Application.Common.Revenue.TerminalSectionSettingsStore.SectionView>>("api/terminal-sections", request);
    public async Task<Result<TenantOperationSettingsDto>> GetAsync() =>
        await GetAsync<TenantOperationSettingsDto>("api/operation-settings");

    public async Task<Result<OperationSetupOverviewDto>> GetSetupAsync() =>
        await GetAsync<OperationSetupOverviewDto>("api/operation-settings/setup");

    public async Task<Result<AddTenantOperationResultDto>> AddAsync(string operationCode, AddTenantOperationRequest request) =>
        await PostAsync<AddTenantOperationRequest, AddTenantOperationResultDto>(
            $"api/operation-settings/{Uri.EscapeDataString(operationCode)}/add", request);

    public async Task<Result<TenantOperationSettingsDto>> UpdateAsync(string operationCode, UpdateTenantOperationSettingRequest request) =>
        await PutAsync<UpdateTenantOperationSettingRequest, TenantOperationSettingsDto>(
            $"api/operation-settings/{Uri.EscapeDataString(operationCode)}", request);
}
