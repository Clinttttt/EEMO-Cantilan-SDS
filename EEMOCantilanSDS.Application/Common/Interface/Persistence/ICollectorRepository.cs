using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ICollectorRepository
{
    Task<CollectorUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CollectorUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default);
    Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default);
    Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default);
    Task AddFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default);
}
