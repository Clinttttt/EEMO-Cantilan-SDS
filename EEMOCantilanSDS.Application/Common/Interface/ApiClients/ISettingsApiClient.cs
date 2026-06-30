using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ISettingsApiClient
{
    Task<Result<SystemSettingsDto>> GetSystemSettingsAsync();
}
