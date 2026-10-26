using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IOperationSettingsApiClient
{
    Task<Result<TenantOperationSettingsDto>> GetAsync();
    Task<Result<IReadOnlyList<EEMOCantilanSDS.Application.Common.Revenue.TerminalSectionSettingsStore.SectionView>>> GetTerminalSectionsAsync();
    Task<Result<IReadOnlyList<EEMOCantilanSDS.Application.Common.Revenue.TerminalSectionSettingsStore.SectionView>>> UpdateTerminalSectionAsync(EEMOCantilanSDS.Application.Dtos.Settings.TerminalSectionUpdateRequest request);

    Task<Result<TenantOperationSettingsDto>> UpdateAsync(string operationCode, UpdateTenantOperationSettingRequest request);
    Task<Result<OperationSetupOverviewDto>> GetSetupAsync();
    Task<Result<AddTenantOperationResultDto>> AddAsync(string operationCode, AddTenantOperationRequest request);
}
