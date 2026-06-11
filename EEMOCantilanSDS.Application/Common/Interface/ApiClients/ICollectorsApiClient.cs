using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ICollectorsApiClient
{
    Task<Result<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync();
    Task<Result<CollectorActivityDto>> GetCollectorByIdAsync(Guid id);
    Task<Result<CollectorDto>> CreateCollectorAsync(CreateCollectorCommand command);
    Task<Result<bool>> UpdateCollectorAsync(UpdateCollectorCommand command);
    Task<Result<bool>> ToggleCollectorStatusAsync(Guid id, bool isActive);
    Task<Result<bool>> ResetCollectorPasswordAsync(Guid id, string newPassword);
    Task<Result<string>> GetNextEmployeeIdAsync();
}
