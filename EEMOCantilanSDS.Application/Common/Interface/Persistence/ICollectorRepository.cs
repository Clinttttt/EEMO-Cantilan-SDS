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

    /// <remarks>
    /// The office's roster and activity views live on <see cref="ICollectorReportingQueries"/>; a collector's own app
    /// reads <see cref="ICollectorMobileQueries"/>. What is left here is the ACCOUNT: load one to modify, find one for
    /// login, and rule on uniqueness.
    /// </remarks>
    Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default);
    Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default);
    Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default);
    Task<string> GenerateNextEmployeeIdAsync(CancellationToken cancellationToken = default);
    Task AddFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default);
    Task ReplaceFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default);

    /// <summary>Ids of ACTIVE collectors assigned to a facility (for routing notifications to them).</summary>
    Task<IReadOnlyList<Guid>> GetActiveCollectorIdsByFacilityAsync(FacilityCode facilityCode, CancellationToken cancellationToken = default);
}
