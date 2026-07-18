using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ICollectorsApiClient
{
    Task<Result<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync();
    Task<Result<CollectorActivityDto>> GetCollectorByIdAsync(Guid id);
    Task<Result<CollectorDto>> CreateCollectorAsync(CreateCollectorCommand command);
    Task<Result<bool>> UpdateCollectorAsync(UpdateCollectorCommand command);
    Task<Result<bool>> ToggleCollectorStatusAsync(Guid id, bool isActive);
    Task<Result<bool>> ResetCollectorPasswordAsync(Guid id, string newPassword, string confirmPassword);
    Task<Result<string>> GetNextEmployeeIdAsync();

    /// <summary>Sends a push notification to a collector's devices. Returns the number of devices reached.</summary>
    Task<Result<int>> SendNotificationAsync(Guid collectorId, string title, string body);

    /// <summary>Gets (or rotates) the LGU's collector-app bind link + the app download link.</summary>
    Task<Result<MobileBindLinkDto>> GetCollectorAppLinkAsync(bool rotate = false);
}
