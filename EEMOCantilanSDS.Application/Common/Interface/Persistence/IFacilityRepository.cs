using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IFacilityRepository
{
    Task<Facility?> GetByCodeAsync(FacilityCode facilityCode, CancellationToken ct);
    Task<IReadOnlyDictionary<FacilityCode, string>> GetFacilityNamesAsync(CancellationToken ct);

    /// <summary>
    /// The current tenant's configured facilities with their billing model, active flag, unit (stall)
    /// count, and current fixed rates — for the in-portal Facility Configuration view. Read-only.
    /// </summary>
    Task<IReadOnlyList<ConfiguredFacilityDto>> GetConfiguredFacilitiesAsync(CancellationToken ct);

    Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    Task<IReadOnlyList<FacilitySidebarSummaryDto>> GetSidebarSummariesAsync(int year, int month, CancellationToken ct);
}
