using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ICollectorsApiClient
{
    Task<Result<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync();
    Task<Result<CollectorActivityDto>> GetCollectorByIdAsync(Guid id);
    Task<Result<CollectorDto>> CreateCollectorAsync(CreateCollectorCommand command);
}
