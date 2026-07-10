using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ICollectorRepository
{
    Task<CollectorUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CollectorUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<CollectorUser?> GetByUsernameOrEmployeeIdAsync(string usernameOrEmployeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-scoped login lookup: resolves the username/employee-id WITHIN a specific municipality. Used by
    /// the scoped collector login so a value shared across LGUs resolves to the correct tenant's account
    /// (the global overload would return an arbitrary match).
    /// </summary>
    Task<CollectorUser?> GetByUsernameOrEmployeeIdAsync(string usernameOrEmployeeId, Guid municipalityId, CancellationToken cancellationToken = default);
    Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// The collector's own collection events (paid/partial) across their assigned facilities for a PH
    /// date range, optionally narrowed to one facility. Scoped by CollectorId, so it never leaks others'.
    /// </summary>
    Task<IReadOnlyList<MobileCollectorRecordDto>> GetCollectorRecordsAsync(
        Guid collectorId, FacilityCode? facility, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    Task<MobileCollectorReportDto> GetCollectorReportAsync(
        Guid collectorId, IReadOnlyCollection<FacilityCode> facilities, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The authenticated collector's own profile: account fields plus lifetime collection stats
    /// (all-time recognized collected, distinct active days, assigned-facility count).
    /// </summary>
    Task<MobileCollectorProfileDto?> GetCollectorProfileAsync(Guid collectorId, CancellationToken cancellationToken = default);

    Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default);
    Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default);
    Task<string> GenerateNextEmployeeIdAsync(CancellationToken cancellationToken = default);
    Task AddFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default);
    Task ReplaceFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default);
}
