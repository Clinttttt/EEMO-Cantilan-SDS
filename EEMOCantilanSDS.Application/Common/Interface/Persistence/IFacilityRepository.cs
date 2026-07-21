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

    /// <summary>Adds a new facility row (tenant is stamped on save). Caller guards code-uniqueness first.</summary>
    Task AddFacilityAsync(Facility facility, CancellationToken ct);

    Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    Task<IReadOnlyList<FacilitySidebarSummaryDto>> GetSidebarSummariesAsync(int year, int month, CancellationToken ct);

    /// <summary>
    /// The current tenant's NPM custom sections: the facility registry names UNION any distinct
    /// CustomSectionName already on stalls (legacy), each with its current stall count. Drives the NPM
    /// section tabs and the remove-when-empty guard.
    /// </summary>
    Task<IReadOnlyList<NpmCustomSectionDto>> GetNpmCustomSectionsAsync(CancellationToken ct);
}
